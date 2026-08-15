using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MultiMessenger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImpersonatedManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PrimaryPlatform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CrmOrderNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Managers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Managers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContactIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlatformUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayNameOnPlatform = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContactIdentities_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessengerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BotToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SessionPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastActiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessengerAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessengerAccounts_Managers_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountHealthLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessengerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountHealthLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountHealthLogs_MessengerAccounts_MessengerAccountId",
                        column: x => x.MessengerAccountId,
                        principalTable: "MessengerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Dialogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessengerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastMessageAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dialogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dialogs_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Dialogs_MessengerAccounts_MessengerAccountId",
                        column: x => x.MessengerAccountId,
                        principalTable: "MessengerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DialogId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SenderType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlatformMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsEdited = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SentByManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Dialogs_DialogId",
                        column: x => x.DialogId,
                        principalTable: "Dialogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MimeType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaAttachments_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessageEditHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PreviousText = table.Column<string>(type: "text", nullable: true),
                    ChangedByManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageEditHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageEditHistory_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountHealthLogs_MessengerAccountId_OccurredAt",
                table: "AccountHealthLogs",
                columns: new[] { "MessengerAccountId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityId",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ImpersonatedManagerId_OccurredAt",
                table: "AuditEntries",
                columns: new[] { "ImpersonatedManagerId", "OccurredAt" },
                descending: new[] { false, true },
                filter: "\"ImpersonatedManagerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ManagerId_OccurredAt",
                table: "AuditEntries",
                columns: new[] { "ManagerId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAt",
                table: "AuditEntries",
                column: "OccurredAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_ContactIdentities_ContactId",
                table: "ContactIdentities",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_ContactIdentities_Platform_PlatformUserId",
                table: "ContactIdentities",
                columns: new[] { "Platform", "PlatformUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_CrmOrderNumber",
                table: "Contacts",
                column: "CrmOrderNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Dialogs_ContactId_MessengerAccountId",
                table: "Dialogs",
                columns: new[] { "ContactId", "MessengerAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dialogs_LastMessageAt",
                table: "Dialogs",
                column: "LastMessageAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Dialogs_MessengerAccountId",
                table: "Dialogs",
                column: "MessengerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Managers_PhoneNumber",
                table: "Managers",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAttachments_MessageId",
                table: "MediaAttachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAttachments_StorageKey",
                table: "MediaAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageEditHistory_ChangedByManagerId_ChangedAt",
                table: "MessageEditHistory",
                columns: new[] { "ChangedByManagerId", "ChangedAt" },
                descending: new[] { false, true },
                filter: "\"ChangedByManagerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEditHistory_MessageId_ChangedAt",
                table: "MessageEditHistory",
                columns: new[] { "MessageId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_DialogId_CreatedAt",
                table: "Messages",
                columns: new[] { "DialogId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_DialogId_PlatformMessageId",
                table: "Messages",
                columns: new[] { "DialogId", "PlatformMessageId" },
                unique: true,
                filter: "\"PlatformMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SentByManagerId",
                table: "Messages",
                column: "SentByManagerId",
                filter: "\"SentByManagerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Status",
                table: "Messages",
                column: "Status",
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_MessengerAccounts_ManagerId_Platform",
                table: "MessengerAccounts",
                columns: new[] { "ManagerId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessengerAccounts_Status",
                table: "MessengerAccounts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountHealthLogs");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "ContactIdentities");

            migrationBuilder.DropTable(
                name: "MediaAttachments");

            migrationBuilder.DropTable(
                name: "MessageEditHistory");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Dialogs");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "MessengerAccounts");

            migrationBuilder.DropTable(
                name: "Managers");
        }
    }
}
