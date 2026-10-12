using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VacationPlanner.Infrastructure.Migrations.Audit
{
    /// <inheritdoc />
    public partial class UniqueAuditLogKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_RequestId_Action_Detail",
                table: "AuditLogs",
                columns: new[] { "RequestId", "Action", "Detail" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_RequestId_Action_Detail",
                table: "AuditLogs");
        }
    }
}
