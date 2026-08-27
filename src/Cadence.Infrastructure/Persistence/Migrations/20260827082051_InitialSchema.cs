using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Cadence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "Athletes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MaxHeartRate = table.Column<int>(type: "integer", nullable: true),
                    RestingHeartRate = table.Column<int>(type: "integer", nullable: true),
                    BirthYear = table.Column<int>(type: "integer", nullable: true),
                    WeightKilograms = table.Column<double>(type: "double precision", nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, computedColumnSql: "lower(\"Email\")", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Athletes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AthleteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sport = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceFormat = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    SourceChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ElapsedTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    MovingTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    DistanceMeters = table.Column<double>(type: "double precision", nullable: false),
                    ElevationGainMeters = table.Column<double>(type: "double precision", nullable: false),
                    ElevationLossMeters = table.Column<double>(type: "double precision", nullable: false),
                    AveragePaceSecondsPerKm = table.Column<double>(type: "double precision", nullable: false),
                    GradeAdjustedPaceSecondsPerKm = table.Column<double>(type: "double precision", nullable: false),
                    AverageHeartRateBpm = table.Column<int>(type: "integer", nullable: true),
                    MaxHeartRateBpm = table.Column<int>(type: "integer", nullable: true),
                    AverageCadenceRpm = table.Column<int>(type: "integer", nullable: true),
                    AveragePowerWatts = table.Column<int>(type: "integer", nullable: true),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    DiscardedSampleCount = table.Column<int>(type: "integer", nullable: false),
                    Route = table.Column<LineString>(type: "geometry(LineString, 4326)", nullable: true),
                    SimplifiedRoute = table.Column<LineString>(type: "geometry(LineString, 4326)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Activities_Athletes_AthleteId",
                        column: x => x.AthleteId,
                        principalTable: "Athletes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoachingReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AthleteId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Verdict = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActivityCount = table.Column<int>(type: "integer", nullable: false),
                    Findings = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachingReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoachingReports_Athletes_AthleteId",
                        column: x => x.AthleteId,
                        principalTable: "Athletes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActivitySamples",
                columns: table => new
                {
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ElapsedSeconds = table.Column<double>(type: "double precision", nullable: false),
                    Location = table.Column<Point>(type: "geometry(Point, 4326)", nullable: false),
                    CumulativeDistanceMeters = table.Column<double>(type: "double precision", nullable: false),
                    AltitudeMeters = table.Column<double>(type: "double precision", nullable: true),
                    HeartRateBpm = table.Column<int>(type: "integer", nullable: true),
                    CadenceRpm = table.Column<int>(type: "integer", nullable: true),
                    PowerWatts = table.Column<int>(type: "integer", nullable: true),
                    SpeedMetersPerSecond = table.Column<double>(type: "double precision", nullable: true),
                    TemperatureCelsius = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivitySamples", x => new { x.ActivityId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_ActivitySamples_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActivitySplits",
                columns: table => new
                {
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    DistanceMeters = table.Column<double>(type: "double precision", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    PaceSecondsPerKm = table.Column<double>(type: "double precision", nullable: false),
                    GradeAdjustedPaceSecondsPerKm = table.Column<double>(type: "double precision", nullable: false),
                    ElevationGainMeters = table.Column<double>(type: "double precision", nullable: false),
                    AverageHeartRateBpm = table.Column<int>(type: "integer", nullable: true),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivitySplits", x => new { x.ActivityId, x.Number });
                    table.ForeignKey(
                        name: "FK_ActivitySplits_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_AthleteId_SourceChecksum",
                table: "Activities",
                columns: new[] { "AthleteId", "SourceChecksum" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Activities_AthleteId_StartedAt",
                table: "Activities",
                columns: new[] { "AthleteId", "StartedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_Route",
                table: "Activities",
                column: "Route")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_Athletes_Email_Lower",
                table: "Athletes",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoachingReports_AthleteId_GeneratedAt",
                table: "CoachingReports",
                columns: new[] { "AthleteId", "GeneratedAt" },
                descending: new[] { false, true });

            // EF cannot model an index on an expression, and FindNearAsync emits
            // ST_DWithin(route::geography, ...) so that the radius is metres on the
            // spheroid rather than degrees. A GiST index on the bare geometry column
            // cannot serve a predicate on the casted expression: the planner falls
            // back to a sequential scan, which is correct and linear, and nothing
            // looks wrong until the table is big. This index matches the expression
            // the query actually uses.
            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_Activities_Route_Geography"
                ON "Activities"
                USING GIST (CAST("Route" AS geography));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Activities_Route_Geography";""");

            migrationBuilder.DropTable(
                name: "ActivitySamples");

            migrationBuilder.DropTable(
                name: "ActivitySplits");

            migrationBuilder.DropTable(
                name: "CoachingReports");

            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "Athletes");
        }
    }
}
