using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false, collation: "NOCASE"),
                    Framework = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PackageManager = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RunCommand = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    BuildCommand = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    TestCommand = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    DefaultPort = table.Column<int>(type: "INTEGER", nullable: true),
                    IsGitRepository = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastOpenedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperProjects", x => x.Id);
                    table.CheckConstraint("CK_DeveloperProjects_DefaultPort", "DefaultPort IS NULL OR (DefaultPort >= 1 AND DefaultPort <= 65535)");
                });

            migrationBuilder.CreateTable(
                name: "SavedCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Command = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    WorkingDirectory = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedCommands_DeveloperProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "DeveloperProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_Key",
                table: "AppSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperProjects_Path",
                table: "DeveloperProjects",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedCommands_ProjectId",
                table: "SavedCommands",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "SavedCommands");

            migrationBuilder.DropTable(
                name: "DeveloperProjects");
        }
    }
}
