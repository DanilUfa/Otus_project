using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VacationPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReliableDeliveryAndLeaveRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "LeaveRequests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "LeaveRequests");
        }
    }
}
