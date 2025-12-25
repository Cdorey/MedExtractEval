using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedExtractEval.Data.Migrations.MedEval
{
    /// <inheritdoc />
    public partial class TryFixModelExtractionClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CaseAssignments_Raters_RaterId",
                table: "CaseAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_ModelExtractions_Cases_CaseItemId",
                table: "ModelExtractions");

            migrationBuilder.DropIndex(
                name: "IX_ModelExtractions_CaseItemId",
                table: "ModelExtractions");

            migrationBuilder.DropIndex(
                name: "IX_CaseAssignments_CaseId_Round",
                table: "CaseAssignments");

            migrationBuilder.DropColumn(
                name: "CaseItemId",
                table: "ModelExtractions");

            migrationBuilder.AlterColumn<Guid>(
                name: "RaterId",
                table: "CaseAssignments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<DateTime>(
                name: "AssignedAt",
                table: "CaseAssignments",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.CreateIndex(
                name: "IX_ModelExtractions_CaseId",
                table: "ModelExtractions",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseAssignments_CaseId_Round",
                table: "CaseAssignments",
                columns: new[] { "CaseId", "Round" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CaseAssignments_Raters_RaterId",
                table: "CaseAssignments",
                column: "RaterId",
                principalTable: "Raters",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ModelExtractions_Cases_CaseId",
                table: "ModelExtractions",
                column: "CaseId",
                principalTable: "Cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CaseAssignments_Raters_RaterId",
                table: "CaseAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_ModelExtractions_Cases_CaseId",
                table: "ModelExtractions");

            migrationBuilder.DropIndex(
                name: "IX_ModelExtractions_CaseId",
                table: "ModelExtractions");

            migrationBuilder.DropIndex(
                name: "IX_CaseAssignments_CaseId_Round",
                table: "CaseAssignments");

            migrationBuilder.AddColumn<Guid>(
                name: "CaseItemId",
                table: "ModelExtractions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RaterId",
                table: "CaseAssignments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "AssignedAt",
                table: "CaseAssignments",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelExtractions_CaseItemId",
                table: "ModelExtractions",
                column: "CaseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseAssignments_CaseId_Round",
                table: "CaseAssignments",
                columns: new[] { "CaseId", "Round" },
                unique: true,
                filter: "[Status] = 'Assigned'");

            migrationBuilder.AddForeignKey(
                name: "FK_CaseAssignments_Raters_RaterId",
                table: "CaseAssignments",
                column: "RaterId",
                principalTable: "Raters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ModelExtractions_Cases_CaseItemId",
                table: "ModelExtractions",
                column: "CaseItemId",
                principalTable: "Cases",
                principalColumn: "Id");
        }
    }
}
