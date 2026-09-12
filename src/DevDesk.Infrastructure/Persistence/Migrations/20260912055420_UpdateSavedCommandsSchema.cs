using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSavedCommandsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "SavedCommands",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "Command",
                table: "SavedCommands",
                newName: "Executable");

            migrationBuilder.AlterColumn<string>(
                name: "WorkingDirectory",
                table: "SavedCommands",
                type: "TEXT",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 1024);

            migrationBuilder.AddColumn<string>(
                name: "Arguments",
                table: "SavedCommands",
                type: "TEXT",
                maxLength: 8192,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "SavedCommands",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "SavedCommands",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "SavedCommands",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "SavedCommands",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            // Safe backfill for legacy rows
            migrationBuilder.Sql("UPDATE SavedCommands SET Arguments = '[]' WHERE Arguments = '' OR Arguments IS NULL;");
            migrationBuilder.Sql("UPDATE SavedCommands SET CreatedAtUtc = UpdatedAtUtc WHERE CreatedAtUtc = '0001-01-01 00:00:00+00:00';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Arguments",
                table: "SavedCommands");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "SavedCommands");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "SavedCommands");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "SavedCommands");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "SavedCommands");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "SavedCommands",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "Executable",
                table: "SavedCommands",
                newName: "Command");

            migrationBuilder.AlterColumn<string>(
                name: "WorkingDirectory",
                table: "SavedCommands",
                type: "TEXT",
                maxLength: 1024,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 2048,
                oldNullable: true);
        }
    }
}
