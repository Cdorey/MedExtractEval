using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedExtractEval.Data.Migrations.MedEval
{
    /// <inheritdoc />
    public partial class AddLeaseAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CaseAssignments_CaseId_RaterId_Round",
                table: "CaseAssignments");

            migrationBuilder.AddColumn<int>(
                name: "Attempt",
                table: "CaseAssignments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CaseItemId",
                table: "CaseAssignments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "CaseAssignments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "CaseAssignments",
                type: "datetime2",
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_CaseAssignments_RaterId",
                table: "CaseAssignments",
                column: "RaterId");

            migrationBuilder.AddForeignKey(
                name: "FK_CaseAssignments_Cases_CaseItemId",
                table: "CaseAssignments",
                column: "CaseItemId",
                principalTable: "Cases",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CaseAssignments_Raters_RaterId",
                table: "CaseAssignments",
                column: "RaterId",
                principalTable: "Raters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CaseAssignments_Cases_CaseItemId",
                table: "CaseAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_CaseAssignments_Raters_RaterId",
                table: "CaseAssignments");

            migrationBuilder.DropIndex(
                name: "IX_CaseAssignments_CaseId_Round",
                table: "CaseAssignments");

            migrationBuilder.DropIndex(
                name: "IX_CaseAssignments_CaseItemId",
                table: "CaseAssignments");

            migrationBuilder.DropIndex(
                name: "IX_CaseAssignments_RaterId",
                table: "CaseAssignments");

            migrationBuilder.DropColumn(
                name: "Attempt",
                table: "CaseAssignments");

            migrationBuilder.DropColumn(
                name: "CaseItemId",
                table: "CaseAssignments");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "CaseAssignments");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "CaseAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_CaseAssignments_CaseId_RaterId_Round",
                table: "CaseAssignments",
                columns: new[] { "CaseId", "RaterId", "Round" },
                unique: true);
        }
    }
}
