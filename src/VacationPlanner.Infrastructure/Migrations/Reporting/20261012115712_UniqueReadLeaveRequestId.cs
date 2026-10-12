using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VacationPlanner.Infrastructure.Migrations.Reporting
{
    /// <inheritdoc />
    public partial class UniqueReadLeaveRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ReadLeaves_RequestId",
                table: "ReadLeaves",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReadLeaves_RequestId",
                table: "ReadLeaves");
        }
    }
}
