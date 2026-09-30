using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AffiliateOptimizer.Repository.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedCursors",
                columns: table => new
                {
                    CredentialFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Cursor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastPolledAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedCursors", x => x.CredentialFingerprint);
                });

            migrationBuilder.CreateTable(
                name: "LoopEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: true),
                    Loop = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClickId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RetryAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Dismissible = table.Column<bool>(type: "bit", nullable: false),
                    DismissedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoopEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrphanDisclosures",
                columns: table => new
                {
                    ClickId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MerchantId = table.Column<int>(type: "int", nullable: false),
                    RoutedNetworkId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RoutedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DisclosedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SeenAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrphanDisclosures", x => x.ClickId);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    StoppedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PausedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ModelNamesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Interleaved = table.Column<bool>(type: "bit", nullable: false),
                    ModelParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    CredentialFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AppVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClickOrdinal = table.Column<long>(type: "bigint", nullable: false),
                    ConsecutiveClickFailures = table.Column<int>(type: "int", nullable: false),
                    LastClickIssuedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Clicks",
                columns: table => new
                {
                    ClickId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<long>(type: "bigint", nullable: false),
                    MerchantId = table.Column<int>(type: "int", nullable: false),
                    MerchantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ModelVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DecidedNetworkId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Rationale = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScoresJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RoutedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RoutedNetworkId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RoutedNetworkName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RouteAttempts = table.Column<int>(type: "int", nullable: false),
                    RouteUnconfirmed = table.Column<bool>(type: "bit", nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    DisclosedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clicks", x => x.ClickId);
                    table.ForeignKey(
                        name: "FK_Clicks_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RunMerchantSnapshots",
                columns: table => new
                {
                    RunId = table.Column<int>(type: "int", nullable: false),
                    MerchantId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunMerchantSnapshots", x => new { x.RunId, x.MerchantId });
                    table.ForeignKey(
                        name: "FK_RunMerchantSnapshots_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunNetworkSnapshots",
                columns: table => new
                {
                    RunId = table.Column<int>(type: "int", nullable: false),
                    NetworkId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AdvertisedRate = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    SupportedMerchantIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CoverageCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunNetworkSnapshots", x => new { x.RunId, x.NetworkId });
                    table.ForeignKey(
                        name: "FK_RunNetworkSnapshots_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Clicks_RoutedNetworkId_DisclosedAt",
                table: "Clicks",
                columns: new[] { "RoutedNetworkId", "DisclosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Clicks_RunId_IssuedAt",
                table: "Clicks",
                columns: new[] { "RunId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Clicks_RunId_ModelName",
                table: "Clicks",
                columns: new[] { "RunId", "ModelName" });

            migrationBuilder.CreateIndex(
                name: "IX_LoopEvents_DismissedAt_ResolvedAt_OccurredAt",
                table: "LoopEvents",
                columns: new[] { "DismissedAt", "ResolvedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoopEvents_OccurredAt",
                table: "LoopEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_Status",
                table: "Runs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "Clicks");

            migrationBuilder.DropTable(
                name: "FeedCursors");

            migrationBuilder.DropTable(
                name: "LoopEvents");

            migrationBuilder.DropTable(
                name: "OrphanDisclosures");

            migrationBuilder.DropTable(
                name: "RunMerchantSnapshots");

            migrationBuilder.DropTable(
                name: "RunNetworkSnapshots");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Runs");
        }
    }
}
