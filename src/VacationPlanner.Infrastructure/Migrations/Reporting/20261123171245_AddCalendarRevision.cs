using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VacationPlanner.Infrastructure.Migrations.Reporting
{
    /// <inheritdoc />
    public partial class AddCalendarRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "ReadLeaves",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                table: "ReadLeaves");
        }
    }
}
