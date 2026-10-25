using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Application.Services;

/// <summary>
/// Регистрация с подтверждением кода по почте. Код и данные регистрации живут
/// в Redis-кэше (IDistributedCache) с TTL: «мёртвых» пользователей в БД нет —
/// запись появляется только после успешного подтверждения.
/// </summary>
public class RegistrationService(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IDistributedCache cache,
    IOutboxRepository outbox)
{
    private static readonly TimeSpan DefaultCodeTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    private const int MaxAttempts = 5;

    private static string KeyFor(string email) => "verify:" + email;
    private static string CooldownKeyFor(string email) => "verifyResend:" + email;

    /// <summary>Регистрация: валидация, генерация кода, сохранение в Redis, письмо через outbox.</summary>
    public async Task RequestAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        Validate(email, request.Name, request.Password);

        if (await users.GetByEmailAsync(email, ct) is not null)
            throw new DomainValidationException("Пользователь с таким email уже зарегистрирован.");

        var key = KeyFor(email);
        if (await cache.GetAsync(key, ct) is not null)
            throw new DomainValidationException("Код уже отправлен. Проверьте почту или запросите новый.");

        var payload = new Verification
        {
            Code = GenerateCode(),
            Name = request.Name!.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            Attempts = 0
        };
        await cache.SetStringAsync(key, JsonSerializer.Serialize(payload),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = DefaultCodeTtl }, ct);
        await EmitCodeAsync(email, payload.Code, payload.Name, ct);
        await outbox.SaveAsync(ct);
    }

    /// <summary>Проверка кода: создаёт пользователя и возвращает его (токены выдаёт AuthService).</summary>
    public async Task<User> ConfirmAsync(ConfirmRegistrationRequest request, CancellationToken ct = default)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var key = KeyFor(email);
        var raw = await cache.GetStringAsync(key, ct)
            ?? throw new DomainValidationException("Код не найден или истёк — запросите новый.");

        var data = JsonSerializer.Deserialize<Verification>(raw)!;
        var code = (request.Code ?? "").Trim();

        if (code.Length != 6 || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(data.Code), Encoding.UTF8.GetBytes(code)))
        {
            data.Attempts++;
            if (data.Attempts >= MaxAttempts)
            {
                await cache.RemoveAsync(key, ct);
                throw new DomainValidationException("Слишком много неверных попыток — запросите код заново.");
            }
            await cache.SetStringAsync(key, JsonSerializer.Serialize(data),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = DefaultCodeTtl }, ct);
            throw new DomainValidationException($"Неверный код. Осталось попыток: {MaxAttempts - data.Attempts}.");
        }

        // Пароль уже захэширован при запросе; нового сотрудника приписываем к менеджеру организации.
        var manager = (await users.GetAllAsync(ct)).FirstOrDefault(u => u.Role == UserRole.Manager);
        var user = new User
        {
            Email = email,
            Name = data.Name,
            Role = UserRole.Employee,
            PasswordHash = data.PasswordHash,
            ManagerId = manager?.Id
        };
        await users.AddAsync(user, ct);
        await users.SaveAsync(ct);

        await cache.RemoveAsync(key, ct);
        return user;
    }

    /// <summary>Повторная отправка кода: не чаще раза в минуту (коoldown в Redis).</summary>
    public async Task ResendAsync(ResendRegistrationRequest request, CancellationToken ct = default)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var key = KeyFor(email);

        var cooldownKey = CooldownKeyFor(email);
        if (await cache.GetAsync(cooldownKey, ct) is not null)
            throw new DomainValidationException("Код уже отправлен — повторите запрос через минуту.");

        var raw = await cache.GetStringAsync(key, ct)
            ?? throw new DomainValidationException("Нет активной регистрации — начните заново.");

        var data = JsonSerializer.Deserialize<Verification>(raw)!;
        data.Code = GenerateCode();
        data.Attempts = 0;
        await cache.SetStringAsync(key, JsonSerializer.Serialize(data),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = DefaultCodeTtl }, ct);
        await cache.SetStringAsync(cooldownKey, "1",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ResendCooldown }, ct);

        await EmitCodeAsync(email, data.Code, data.Name, ct);
        await outbox.SaveAsync(ct);
    }

    private async Task EmitCodeAsync(string email, string code, string name, CancellationToken ct)
        => await outbox.AddAsync(new OutboxMessage
        {
            Type = nameof(EmailVerificationRequested),
            Payload = JsonSerializer.Serialize(new EmailVerificationRequested(email, code, name)),
            CreatedAt = DateTime.UtcNow
        }, ct);

    /// <summary>6-значный код из криптографического ГСЧ.</summary>
    private static string GenerateCode()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static void Validate(string email, string name, string password)
    {
        if (!MailRegex.IsMatch(email) || email.Length > 254)
            throw new DomainValidationException("Некорректный email.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            throw new DomainValidationException("Укажите имя (минимум 2 символа).");
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new DomainValidationException("Пароль должен быть не короче 8 символов.");
    }

    private static readonly System.Text.RegularExpressions.Regex MailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private sealed class Verification
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public int Attempts { get; set; }
    }
}
