using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDb.Migrations;

/// <inheritdoc />
public partial class _20261122063005_ScheduleRunStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Convert existing names before SQL Server changes the column type.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [ScheduleRuns] WHERE [Status] NOT IN (N'Running', N'Completed', N'Skipped', N'Interrupted', N'Failed'))
                THROW 50000, 'Cannot migrate an unknown ScheduleRun status.', 1;

            UPDATE [ScheduleRuns]
            SET [Status] = CASE [Status]
                WHEN N'Running' THEN N'0'
                WHEN N'Completed' THEN N'1'
                WHEN N'Skipped' THEN N'2'
                WHEN N'Interrupted' THEN N'3'
                WHEN N'Failed' THEN N'4'
            END;
            """);

        migrationBuilder.AlterColumn<int>(
            name: "Status",
            table: "ScheduleRuns",
            type: "int",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(30)",
            oldMaxLength: 30);

        migrationBuilder.AddCheckConstraint(
            name: "CK_ScheduleRuns_Status",
            table: "ScheduleRuns",
            sql: "[Status] IN (0, 1, 2, 3, 4)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [ScheduleRuns] WHERE [Status] NOT IN (0, 1, 2, 3, 4))
                THROW 50000, 'Cannot roll back an unknown ScheduleRun status.', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_ScheduleRuns_Status",
            table: "ScheduleRuns");

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "ScheduleRuns",
            type: "nvarchar(30)",
            maxLength: 30,
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.Sql("""
            UPDATE [ScheduleRuns]
            SET [Status] = CASE [Status]
                WHEN N'0' THEN N'Running'
                WHEN N'1' THEN N'Completed'
                WHEN N'2' THEN N'Skipped'
                WHEN N'3' THEN N'Interrupted'
                WHEN N'4' THEN N'Failed'
            END;
            """);
    }
}
