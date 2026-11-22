using Microsoft.EntityFrameworkCore.Migrations;
using TaskMan.Domain.Attachments;

#nullable disable

namespace TaskMan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentLimitTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The API checks the limit before uploading, but that check is not
            // atomic with the insert. The trigger makes the limit enforceable at
            // the database level so concurrent uploads cannot exceed it.
            migrationBuilder.Sql($"""
                CREATE FUNCTION enforce_attachment_limit() RETURNS trigger AS $$
                    BEGIN
                        IF NEW."TaskId" IS NOT NULL
                            AND (SELECT COUNT(*) FROM "Attachments" WHERE "TaskId" = NEW."TaskId") >= {AttachmentRules.MaxAttachmentsPerParent}
                        THEN
                            RAISE EXCEPTION 'A task can have at most {AttachmentRules.MaxAttachmentsPerParent} attachments.';
                        END IF;

                        IF NEW."ReplyId" IS NOT NULL
                            AND (SELECT COUNT(*) FROM "Attachments" WHERE "ReplyId" = NEW."ReplyId") >= {AttachmentRules.MaxAttachmentsPerParent}
                        THEN
                            RAISE EXCEPTION 'A reply can have at most {AttachmentRules.MaxAttachmentsPerParent} attachments.';
                        END IF;

                        RETURN NEW;
                    END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER enforce_attachment_limit
                    BEFORE INSERT ON "Attachments"
                    FOR EACH ROW
                    EXECUTE FUNCTION enforce_attachment_limit();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS enforce_attachment_limit ON "Attachments";
                DROP FUNCTION IF EXISTS enforce_attachment_limit();
                """);
        }
    }
}
