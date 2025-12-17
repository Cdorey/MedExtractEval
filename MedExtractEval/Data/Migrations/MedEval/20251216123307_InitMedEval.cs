using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedExtractEval.Data.Migrations.MedEval
{
    /// <inheritdoc />
    public partial class InitMedEval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RawText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TaskType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetaInfo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FinalGoldLabel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FinalGoldRaterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalizedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IncludedCaseIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Experiments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VersionTag = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PromptTemplate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Temperature = table.Column<double>(type: "float", nullable: false),
                    TopP = table.Column<double>(type: "float", nullable: false),
                    IsDeterministic = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Raters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LoginName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsAdmin = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Raters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelExtractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModelConfigId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RawResponse = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParsedValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParsedSuccessfully = table.Column<bool>(type: "bit", nullable: false),
                    PromptTokens = table.Column<int>(type: "int", nullable: false),
                    CompletionTokens = table.Column<int>(type: "int", nullable: false),
                    Latency = table.Column<TimeSpan>(type: "time", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelExtractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelExtractions_Cases_CaseItemId",
                        column: x => x.CaseItemId,
                        principalTable: "Cases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ModelExtractions_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ModelExtractions_ModelConfigs_ModelConfigId",
                        column: x => x.ModelConfigId,
                        principalTable: "ModelConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Annotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnnotatedValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Uncertainty = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RaterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DifficultyScore = table.Column<int>(type: "int", nullable: true),
                    ConfidenceScore = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Annotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Annotations_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Annotations_Raters_RaterId",
                        column: x => x.RaterId,
                        principalTable: "Raters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Annotations_CaseId_RaterId_Round",
                table: "Annotations",
                columns: new[] { "CaseId", "RaterId", "Round" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Annotations_RaterId",
                table: "Annotations",
                column: "RaterId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseAssignments_CaseId_RaterId_Round",
                table: "CaseAssignments",
                columns: new[] { "CaseId", "RaterId", "Round" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelExtractions_CaseItemId",
                table: "ModelExtractions",
                column: "CaseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelExtractions_ExperimentId",
                table: "ModelExtractions",
                column: "ExperimentId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelExtractions_ModelConfigId",
                table: "ModelExtractions",
                column: "ModelConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_Raters_LoginName",
                table: "Raters",
                column: "LoginName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Annotations");

            migrationBuilder.DropTable(
                name: "CaseAssignments");

            migrationBuilder.DropTable(
                name: "ModelExtractions");

            migrationBuilder.DropTable(
                name: "Raters");

            migrationBuilder.DropTable(
                name: "Cases");

            migrationBuilder.DropTable(
                name: "Experiments");

            migrationBuilder.DropTable(
                name: "ModelConfigs");
        }
    }
}
