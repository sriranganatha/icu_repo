using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GNex.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddBrdDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Schema already applied via raw SQL — this migration only syncs the EF snapshot.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible — schema was applied out-of-band.
        }
    }
}
