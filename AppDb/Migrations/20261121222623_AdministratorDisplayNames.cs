using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDb.Migrations;

/// <inheritdoc />
public partial class _20261121222623_AdministratorDisplayNames : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DisplayName",
            table: "AdministratorGrants",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DisplayName",
            table: "AdministratorGrants");
    }
}
