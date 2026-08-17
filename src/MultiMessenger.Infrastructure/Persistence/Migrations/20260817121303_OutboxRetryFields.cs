using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MultiMessenger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxRetryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SendAttempts",
                table: "Messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SendAttempts",
                table: "Messages");
        }
    }
}
