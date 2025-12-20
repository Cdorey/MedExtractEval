using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedExtractEval.Data.Migrations.MedEval
{
    /// <inheritdoc />
    public partial class FixCaseItemIdAndAddAnnotationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CaseAssignments_Cases_CaseItemId",
                table: "CaseAssignments");

            migrationBuilder.DropIndex(
                name: "IX_CaseAssignments_CaseId_Round",
                table: "CaseAssignments");

            migrationBuilder.DropIndex(
                name: "IX_CaseAssignments_CaseItemId",
                table: "CaseAssignments");

            migrationBuilder.DropColumn(
                name: "CaseId",
                table: "CaseAssignments");

            migrationBuilder.AlterColumn<Guid>(
                name: "CaseItemId",
                table: "CaseAssignments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AnnotationId",
                table: "CaseAssignments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseAssignments_AnnotationId",
                table: "CaseAssignments",
                column: "AnnotationId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseAssignments_CaseItemId_Round",
                table: "CaseAssignments",
                columns: new[] { "CaseItemId", "Round" },
                unique: true,
                filter: "[Status] = 'Assigned'");

            migrationBuilder.AddForeignKey(
                name: "FK_CaseAssignments_Annotations_AnnotationId",
                table: "CaseAssignments",
                column: "AnnotationId",
                principalTable: "Annotations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CaseAssignments_Cases_CaseItemId",
                table: "CaseAssignments",
                column: "CaseItemId",
                principalTable: "Cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CaseAssignments_Annotations_AnnotationId",
                table: "CaseAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_CaseAssignments_Cases_CaseItemId",
                table: "CaseAssignments");

            migrationBuilder.DropIndex(
                name: "IX_CaseAssignments_AnnotationId",
                table: "CaseAssignments");

            migrationBuilder.DropIndex(
                name: "IX_CaseAssignments_CaseItemId_Round",
                table: "CaseAssignments");

            migrationBuilder.DropColumn(
                name: "AnnotationId",
                table: "CaseAssignments");

            migrationBuilder.AlterColumn<Guid>(
                name: "CaseItemId",
                table: "CaseAssignments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "CaseId",
                table: "CaseAssignments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_CaseAssignments_CaseId_Round",
                table: "CaseAssignments",
                columns: new[] { "CaseId", "Round" },
                unique: true,
                filter: "[Status] = 'Assigned'");

            migrationBuilder.CreateIndex(
                name: "IX_CaseAssignments_CaseItemId",
                table: "CaseAssignments",
                column: "CaseItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_CaseAssignments_Cases_CaseItemId",
                table: "CaseAssignments",
                column: "CaseItemId",
                principalTable: "Cases",
                principalColumn: "Id");
        }
    }
}
