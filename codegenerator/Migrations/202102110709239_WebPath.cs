namespace WEB.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class WebPath : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Projects", "WebPath", c => c.String(nullable: false, maxLength: 250));
        }
        
        public override void Down()
        {
            DropColumn("dbo.Projects", "WebPath");
        }
    }
}
