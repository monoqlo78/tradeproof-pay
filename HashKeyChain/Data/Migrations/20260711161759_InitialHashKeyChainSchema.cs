using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HashKeyChain.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialHashKeyChainSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hashkeychain");

            migrationBuilder.CreateTable(
                name: "Companies",
                schema: "hashkeychain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WalletAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CountryCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeRequests",
                schema: "hashkeychain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeReference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UiCulture = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "hashkeychain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    PreferredCulture = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "hashkeychain",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                schema: "hashkeychain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BuyerCompanyId = table.Column<int>(type: "int", nullable: false),
                    SellerCompanyId = table.Column<int>(type: "int", nullable: false),
                    BuyerWalletAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SellerWalletAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TransportMode = table.Column<int>(type: "int", nullable: false),
                    PaymentToken = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PaymentAmount = table.Column<decimal>(type: "decimal(38,18)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PurchaseOrderNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExpectedProductDescription = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ExpectedQuantity = table.Column<decimal>(type: "decimal(38,18)", nullable: true),
                    LatestShipmentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DocumentSubmissionDeadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerifierUserId = table.Column<int>(type: "int", nullable: true),
                    BuyerApproverUserId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ConditionsLocked = table.Column<bool>(type: "bit", nullable: false),
                    LatestVerdict = table.Column<int>(type: "int", nullable: true),
                    IsFunded = table.Column<bool>(type: "bit", nullable: false),
                    IsSettled = table.Column<bool>(type: "bit", nullable: false),
                    IsRefunded = table.Column<bool>(type: "bit", nullable: false),
                    SettlementTxHash = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: true),
                    RefundTxHash = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trades_Companies_BuyerCompanyId",
                        column: x => x.BuyerCompanyId,
                        principalSchema: "hashkeychain",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_Companies_SellerCompanyId",
                        column: x => x.SellerCompanyId,
                        principalSchema: "hashkeychain",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_Users_BuyerApproverUserId",
                        column: x => x.BuyerApproverUserId,
                        principalSchema: "hashkeychain",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_Users_VerifierUserId",
                        column: x => x.VerifierUserId,
                        principalSchema: "hashkeychain",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "hashkeychain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "hashkeychain",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "hashkeychain",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BeforeStatus = table.Column<int>(type: "int", nullable: true),
                    AfterStatus = table.Column<int>(type: "int", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TransactionHash = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEntries_Trades_TradeId",
                        column: x => x.TradeId,
                        principalSchema: "hashkeychain",
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChainTransactions",
                schema: "hashkeychain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TransactionHash = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: true),
                    BlockNumber = table.Column<long>(type: "bigint", nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ToAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ContractAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ChainId = table.Column<int>(type: "int", nullable: false),
                    GasUsed = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExplorerUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChainTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChainTransactions_Trades_TradeId",
                        column: x => x.TradeId,
                        principalSchema: "hashkeychain",
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeDocuments",
                schema: "hashkeychain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeDocuments_Trades_TradeId",
                        column: x => x.TradeId,
                        principalSchema: "hashkeychain",
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerificationRuns",
                schema: "hashkeychain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeId = table.Column<int>(type: "int", nullable: false),
                    RunAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Result = table.Column<int>(type: "int", nullable: false),
                    AiRiskLevel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationRuns_Trades_TradeId",
                        column: x => x.TradeId,
                        principalSchema: "hashkeychain",
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentVersions",
                schema: "hashkeychain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeDocumentId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    UploadedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AnalysisStatus = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ExtractedFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    SourceLanguage = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DetectedType = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentVersions_TradeDocuments_TradeDocumentId",
                        column: x => x.TradeDocumentId,
                        principalSchema: "hashkeychain",
                        principalTable: "TradeDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentChecks",
                schema: "hashkeychain",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VerificationRunId = table.Column<int>(type: "int", nullable: false),
                    RuleKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    IsSoft = table.Column<bool>(type: "bit", nullable: false),
                    ExpectedValue = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ActualValue = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentChecks_VerificationRuns_VerificationRunId",
                        column: x => x.VerificationRunId,
                        principalSchema: "hashkeychain",
                        principalTable: "VerificationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TimestampUtc",
                schema: "hashkeychain",
                table: "AuditEntries",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TradeId",
                schema: "hashkeychain",
                table: "AuditEntries",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_ChainTransactions_TradeId",
                schema: "hashkeychain",
                table: "ChainTransactions",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChecks_VerificationRunId",
                schema: "hashkeychain",
                table: "DocumentChecks",
                column: "VerificationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_Sha256",
                schema: "hashkeychain",
                table: "DocumentVersions",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_TradeDocumentId",
                schema: "hashkeychain",
                table: "DocumentVersions",
                column: "TradeDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocuments_TradeId_DocumentType",
                schema: "hashkeychain",
                table: "TradeDocuments",
                columns: new[] { "TradeId", "DocumentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trades_BuyerApproverUserId",
                schema: "hashkeychain",
                table: "Trades",
                column: "BuyerApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_BuyerCompanyId",
                schema: "hashkeychain",
                table: "Trades",
                column: "BuyerCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_SellerCompanyId",
                schema: "hashkeychain",
                table: "Trades",
                column: "SellerCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_TradeReference",
                schema: "hashkeychain",
                table: "Trades",
                column: "TradeReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trades_VerifierUserId",
                schema: "hashkeychain",
                table: "Trades",
                column: "VerifierUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId",
                schema: "hashkeychain",
                table: "UserRoles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CompanyId",
                schema: "hashkeychain",
                table: "Users",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationRuns_TradeId",
                schema: "hashkeychain",
                table: "VerificationRuns",
                column: "TradeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries",
                schema: "hashkeychain");

            migrationBuilder.DropTable(
                name: "ChainTransactions",
                schema: "hashkeychain");

            migrationBuilder.DropTable(
                name: "DocumentChecks",
                schema: "hashkeychain");

            migrationBuilder.DropTable(
                name: "DocumentVersions",
                schema: "hashkeychain");

            migrationBuilder.DropTable(
                name: "TradeRequests",
                schema: "hashkeychain");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "hashkeychain");

            migrationBuilder.DropTable(
                name: "VerificationRuns",
                schema: "hashkeychain");

            migrationBuilder.DropTable(
                name: "TradeDocuments",
                schema: "hashkeychain");

            migrationBuilder.DropTable(
                name: "Trades",
                schema: "hashkeychain");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "hashkeychain");

            migrationBuilder.DropTable(
                name: "Companies",
                schema: "hashkeychain");
        }
    }
}
