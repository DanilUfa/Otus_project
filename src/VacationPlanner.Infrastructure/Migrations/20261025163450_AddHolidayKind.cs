using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VacationPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHolidayKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Holidays",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Holidays");
        }
    }
}
