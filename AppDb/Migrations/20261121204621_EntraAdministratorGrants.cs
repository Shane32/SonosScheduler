using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDb.Migrations;

/// <inheritdoc />
public partial class _20261121204621_EntraAdministratorGrants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Admins");

        migrationBuilder.CreateTable(
            name: "AdministratorGrants",
            columns: table => new {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_AdministratorGrants", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AdministratorGrants_TenantId_ObjectId",
            table: "AdministratorGrants",
            columns: new[] { "TenantId", "ObjectId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AdministratorGrants");

        migrationBuilder.CreateTable(
            name: "Admins",
            columns: table => new {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                SecurityStamp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_Admins", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Admins_NormalizedName",
            table: "Admins",
            column: "NormalizedName",
            unique: true);
    }
}
