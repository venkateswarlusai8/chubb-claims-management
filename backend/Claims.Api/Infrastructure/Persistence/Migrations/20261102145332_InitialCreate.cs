using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Claims.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ClaimantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ClaimantName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ClaimantEmail = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    PolicyNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MarketCode = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IncidentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    SettledAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AssignedOfficerId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AssignedOfficerName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Claims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecipientId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ClaimId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SourceEventId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Consumer = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedEvents", x => new { x.EventId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "ClaimHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ActorRole = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimHistory_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InformationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    RequestedById = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequestedByName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Response = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InformationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InformationRequests_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimHistory_ClaimId",
                table: "ClaimHistory",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ClaimantId",
                table: "Claims",
                column: "ClaimantId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ClaimNumber",
                table: "Claims",
                column: "ClaimNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Claims_Status_AssignedOfficerId",
                table: "Claims",
                columns: new[] { "Status", "AssignedOfficerId" });

            migrationBuilder.CreateIndex(
                name: "IX_InformationRequests_ClaimId",
                table: "InformationRequests",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientId_CreatedAt",
                table: "Notifications",
                columns: new[] { "RecipientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt_OccurredAt",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimHistory");

            migrationBuilder.DropTable(
                name: "InformationRequests");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "ProcessedEvents");

            migrationBuilder.DropTable(
                name: "Claims");
        }
    }
}
