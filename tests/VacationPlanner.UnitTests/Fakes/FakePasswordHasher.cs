using VacationPlanner.Application.Contracts;

namespace VacationPlanner.UnitTests.Fakes;

/// <summary>
/// Детерминированный хэш для unit-тестов: Hash(p) = \"PBKDF2:fake:\"+p.
/// NeedsRehash включается флагом — эмулирует legacy-хэш раннего черновика.
/// </summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public bool NeedsRehashResult { get; set; }
    public int HashCalls { get; private set; }
    public int VerifyCalls { get; private set; }

    public string Hash(string password)
    {
        HashCalls++;
        return "PBKDF2:fake:" + password;
    }

    public bool Verify(string password, string hash)
    {
        VerifyCalls++;
        return hash == "PBKDF2:fake:" + password;
    }

    public bool NeedsRehash(string hash) => NeedsRehashResult;
}
