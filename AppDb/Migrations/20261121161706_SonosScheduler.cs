using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDb.Migrations;

/// <inheritdoc />
public partial class _20261121161706_SonosScheduler : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Comments");

        migrationBuilder.DropTable(
            name: "Posts");

        migrationBuilder.DropTable(
            name: "Users");

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

        migrationBuilder.CreateTable(
            name: "Groups",
            columns: table => new {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Subnet = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                SeedAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                LastRefreshedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table => {
                table.PrimaryKey("PK_Groups", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Logs",
            columns: table => new {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ScheduleId = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table => {
                table.PrimaryKey("PK_Logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ScheduleRuns",
            columns: table => new {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ScheduleId = table.Column<int>(type: "int", nullable: false),
                ScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_ScheduleRuns", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Schedules",
            columns: table => new {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                GroupId = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                DaysOfWeek = table.Column<int>(type: "int", nullable: false),
                LocalTime = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Enabled = table.Column<bool>(type: "bit", nullable: false),
                DisabledUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LibraryObjectId = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                LibraryTitle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                SelectionMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                OnlyIfIdle = table.Column<bool>(type: "bit", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_Schedules", x => x.Id);
                table.ForeignKey(
                    name: "FK_Schedules_Groups_GroupId",
                    column: x => x.GroupId,
                    principalTable: "Groups",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Speakers",
            columns: table => new {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                GroupId = table.Column<int>(type: "int", nullable: false),
                SonosId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                DescriptionUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                CoordinatorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                IsOnline = table.Column<bool>(type: "bit", nullable: false),
                State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Title = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Artist = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Album = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Volume = table.Column<int>(type: "int", nullable: false),
                Muted = table.Column<bool>(type: "bit", nullable: false),
                LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                StatusUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table => {
                table.PrimaryKey("PK_Speakers", x => x.Id);
                table.ForeignKey(
                    name: "FK_Speakers_Groups_GroupId",
                    column: x => x.GroupId,
                    principalTable: "Groups",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ScheduleExclusions",
            columns: table => new {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ScheduleId = table.Column<int>(type: "int", nullable: false),
                Date = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
            },
            constraints: table => {
                table.PrimaryKey("PK_ScheduleExclusions", x => x.Id);
                table.ForeignKey(
                    name: "FK_ScheduleExclusions_Schedules_ScheduleId",
                    column: x => x.ScheduleId,
                    principalTable: "Schedules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ScheduleSpeakers",
            columns: table => new {
                ScheduleId = table.Column<int>(type: "int", nullable: false),
                SpeakerId = table.Column<int>(type: "int", nullable: false),
                Enabled = table.Column<bool>(type: "bit", nullable: false),
                Volume = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table => {
                table.PrimaryKey("PK_ScheduleSpeakers", x => new { x.ScheduleId, x.SpeakerId });
                table.ForeignKey(
                    name: "FK_ScheduleSpeakers_Schedules_ScheduleId",
                    column: x => x.ScheduleId,
                    principalTable: "Schedules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ScheduleSpeakers_Speakers_SpeakerId",
                    column: x => x.SpeakerId,
                    principalTable: "Speakers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Admins_NormalizedName",
            table: "Admins",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Logs_Timestamp",
            table: "Logs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_ScheduleExclusions_ScheduleId",
            table: "ScheduleExclusions",
            column: "ScheduleId");

        migrationBuilder.CreateIndex(
            name: "IX_ScheduleRuns_ScheduleId_ScheduledFor",
            table: "ScheduleRuns",
            columns: new[] { "ScheduleId", "ScheduledFor" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Schedules_GroupId",
            table: "Schedules",
            column: "GroupId");

        migrationBuilder.CreateIndex(
            name: "IX_ScheduleSpeakers_SpeakerId",
            table: "ScheduleSpeakers",
            column: "SpeakerId");

        migrationBuilder.CreateIndex(
            name: "IX_Speakers_GroupId_SonosId",
            table: "Speakers",
            columns: new[] { "GroupId", "SonosId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Admins");

        migrationBuilder.DropTable(
            name: "Logs");

        migrationBuilder.DropTable(
            name: "ScheduleExclusions");

        migrationBuilder.DropTable(
            name: "ScheduleRuns");

        migrationBuilder.DropTable(
            name: "ScheduleSpeakers");

        migrationBuilder.DropTable(
            name: "Schedules");

        migrationBuilder.DropTable(
            name: "Speakers");

        migrationBuilder.DropTable(
            name: "Groups");

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                FirstName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                JwtSubject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                LastName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                Roles = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_Users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Posts",
            columns: table => new {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<int>(type: "int", nullable: false),
                Content = table.Column<string>(type: "nvarchar(max)", maxLength: 2147483647, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_Posts", x => x.Id);
                table.ForeignKey(
                    name: "FK_Posts_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Comments",
            columns: table => new {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PostId = table.Column<int>(type: "int", nullable: false),
                UserId = table.Column<int>(type: "int", nullable: false),
                Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_Comments", x => x.Id);
                table.ForeignKey(
                    name: "FK_Comments_Posts_PostId",
                    column: x => x.PostId,
                    principalTable: "Posts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Comments_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Comments_PostId",
            table: "Comments",
            column: "PostId");

        migrationBuilder.CreateIndex(
            name: "IX_Comments_UserId",
            table: "Comments",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_Posts_UserId",
            table: "Posts",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_Users_JwtSubject",
            table: "Users",
            column: "JwtSubject",
            unique: true);
    }
}
