using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedExtractEval.Data.Migrations.MedEval
{
    /// <inheritdoc />
    public partial class TryFixCaseId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CaseAssignments_Cases_CaseItemId",
                table: "CaseAssignments");

            migrationBuilder.RenameColumn(
                name: "CaseItemId",
                table: "CaseAssignments",
                newName: "CaseId");

            migrationBuilder.RenameIndex(
                name: "IX_CaseAssignments_CaseItemId_Round",
                table: "CaseAssignments",
                newName: "IX_CaseAssignments_CaseId_Round");

            migrationBuilder.AddForeignKey(
                name: "FK_CaseAssignments_Cases_CaseId",
                table: "CaseAssignments",
                column: "CaseId",
                principalTable: "Cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CaseAssignments_Cases_CaseId",
                table: "CaseAssignments");

            migrationBuilder.RenameColumn(
                name: "CaseId",
                table: "CaseAssignments",
                newName: "CaseItemId");

            migrationBuilder.RenameIndex(
                name: "IX_CaseAssignments_CaseId_Round",
                table: "CaseAssignments",
                newName: "IX_CaseAssignments_CaseItemId_Round");

            migrationBuilder.AddForeignKey(
                name: "FK_CaseAssignments_Cases_CaseItemId",
                table: "CaseAssignments",
                column: "CaseItemId",
                principalTable: "Cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
