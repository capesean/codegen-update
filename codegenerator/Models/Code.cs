using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Data.Entity;
using System.Data;
using System.Data.Entity.Infrastructure;
using System.IO;

/*
 notes on error: Possibly unhandled rejection:
 as soon as you work with the .$promise, you have to include another .catch() 
 if you don't, then you get the error. 
     */

namespace WEB.Models
{
    public class Code
    {
        private Entity CurrentEntity { get; set; }
        private List<Entity> _allEntities { get; set; }
        private List<Entity> NormalEntities { get { return AllEntities.Where(e => e.EntityType == EntityType.Normal && !e.Exclude).ToList(); } }
        private List<Entity> AllEntities
        {
            get
            {
                if (_allEntities == null)
                {
                    _allEntities = DbContext.Entities
                        .Include(e => e.Project)
                        .Include(e => e.Fields)
                        .Include(e => e.CodeReplacements)
                        .Include(e => e.RelationshipsAsChild.Select(p => p.RelationshipFields))
                        .Include(e => e.RelationshipsAsChild.Select(p => p.ParentEntity))
                        .Include(e => e.RelationshipsAsParent.Select(p => p.RelationshipFields))
                        .Include(e => e.RelationshipsAsParent.Select(p => p.ChildEntity))
                        .Where(e => e.ProjectId == CurrentEntity.ProjectId).OrderBy(e => e.Name).ToList();
                }
                return _allEntities;
            }
        }
        private List<Lookup> _lookups { get; set; }
        private List<Lookup> Lookups
        {
            get
            {
                if (_lookups == null)
                {
                    _lookups = DbContext.Lookups.Include(l => l.LookupOptions).Where(l => l.ProjectId == CurrentEntity.ProjectId).OrderBy(l => l.Name).ToList();
                }
                return _lookups;
            }
        }
        private List<CodeReplacement> _codeReplacements { get; set; }
        private List<CodeReplacement> CodeReplacements
        {
            get
            {
                if (_codeReplacements == null)
                {
                    _codeReplacements = DbContext.CodeReplacements.Include(cr => cr.Entity).Where(cr => cr.Entity.ProjectId == CurrentEntity.ProjectId).OrderBy(cr => cr.SortOrder).ToList();
                }
                return _codeReplacements;
            }
        }
        private List<RelationshipField> _relationshipFields { get; set; }
        private List<RelationshipField> RelationshipFields
        {
            get
            {
                if (_relationshipFields == null)
                {
                    _relationshipFields = DbContext.RelationshipFields.Where(rf => rf.Relationship.ParentEntity.ProjectId == CurrentEntity.ProjectId).ToList();
                }
                return _relationshipFields;
            }
        }
        private ApplicationDbContext DbContext { get; set; }
        private Entity GetEntity(Guid entityId)
        {
            return AllEntities.Single(e => e.EntityId == entityId);
        }
        private Relationship ParentHierarchyRelationship
        {
            get { return CurrentEntity.RelationshipsAsChild.SingleOrDefault(r => r.Hierarchy); }
        }

        public Code(Entity currentEntity, ApplicationDbContext dbContext)
        {
            CurrentEntity = currentEntity;
            DbContext = dbContext;
        }

        public string GenerateModel()
        {
            //if (CurrentEntity.EntityType == EntityType.User) throw new NotImplementedException("Not Implemented: GenerateModel for User entity");

            var s = new StringBuilder();
            s.Add($"using System;");
            if (CurrentEntity.RelationshipsAsParent.Any())
                s.Add($"using System.Collections.Generic;");
            s.Add($"using System.ComponentModel.DataAnnotations;");
            s.Add($"using System.ComponentModel.DataAnnotations.Schema;"); // decimals
            s.Add($"");
            s.Add($"namespace {CurrentEntity.Project.Namespace}.Models");
            s.Add($"{{");
            s.Add($"    public {(CurrentEntity.PartialEntityClass ? "partial " : string.Empty)}class {CurrentEntity.Name}");
            s.Add($"    {{");

            // fields
            var keyCounter = 0;
            foreach (var field in CurrentEntity.Fields.OrderBy(f => f.FieldOrder))
            {
                if (field.KeyField && CurrentEntity.EntityType == EntityType.User) continue;

                var attributes = new List<string>();

                if (field.KeyField && CurrentEntity.KeyFields.Count == 1)
                {
                    attributes.Add("Key");
                    // probably shouldn't include decimals etc...
                    if (CurrentEntity.KeyFields.Count == 1 && field.CustomType == CustomType.Number)
                        attributes.Add("DatabaseGenerated(DatabaseGeneratedOption.Identity)");
                    if (CurrentEntity.KeyFields.Count > 1)
                        attributes.Add($"Column(Order = {keyCounter})");

                    keyCounter++;
                }

                if (field.EditPageType == EditPageType.CalculatedField)
                    attributes.Add("DatabaseGenerated(DatabaseGeneratedOption.Computed)");
                else
                {
                    if (!field.IsNullable)
                    {
                        if (field.CustomType == CustomType.String)
                            attributes.Add("Required(AllowEmptyStrings = true)");
                        else
                            attributes.Add("Required");
                    }

                    if (field.NetType == "string")
                    {
                        if (field.Length == 0 && (field.FieldType == FieldType.Varchar || field.FieldType == FieldType.nVarchar))
                        {
                            //?
                        }
                        else if (field.Length > 0)
                        {
                            attributes.Add($"MaxLength({field.Length}){(field.MinLength > 0 ? $", MinLength({ field.MinLength})" : "")}");
                        }
                    }
                    else if (field.FieldType == FieldType.Date)
                        attributes.Add($"Column(TypeName = \"Date\")");
                    else if (field.NetType == "decimal" && field.EditPageType != EditPageType.CalculatedField)
                        attributes.Add($"Column(TypeName = \"decimal({field.Precision}, {field.Scale})\")");
                }

                s.Add($"        [" + string.Join(", ", attributes) + "]");

                if (field.EditPageType == EditPageType.CalculatedField)
                {
                    s.Add($"        public {field.NetType.ToString()} {field.Name} {{ get; private set; }}");
                }
                else
                {
                    s.Add($"        public {field.NetType.ToString()} {field.Name} {{ get; set; }}");
                }
                s.Add($"");
            }

            // child entities
            foreach (var relationship in CurrentEntity.RelationshipsAsParent.Where(r => !r.ChildEntity.Exclude).OrderBy(o => o.SortOrder))
            {
                s.Add($"        public virtual ICollection<{GetEntity(relationship.ChildEntityId).Name}> {relationship.CollectionName} {{ get; set; }} = new List<{GetEntity(relationship.ChildEntityId).Name}>();");
                s.Add($"");
            }

            // parent entities
            foreach (var relationship in CurrentEntity.RelationshipsAsChild.Where(r => !r.ParentEntity.Exclude).OrderBy(o => o.ParentEntity.Name))
            {
                if (relationship.RelationshipFields.Count() == 1)
                    s.Add($"        [ForeignKey(\"" + relationship.RelationshipFields.Single().ChildField.Name + "\")]");
                s.Add($"        public virtual {GetEntity(relationship.ParentEntityId).Name} {relationship.ParentName} {{ get; set; }}");
                s.Add($"");
            }

            // constructor
            if (CurrentEntity.KeyFields.Any(f => f.KeyField && f.FieldType == FieldType.Guid))
            {
                s.Add($"        public {CurrentEntity.Name}()");
                s.Add($"        {{");
                foreach (var field in CurrentEntity.KeyFields)
                {
                    // where the primary key is a composite with the guid being a fk, don't init the field. e.g. IXESHA.ConsultantMonths.ConsultantId (+Year+Month)
                    if (CurrentEntity.KeyFields.Count > 1 && CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(rf => rf.ChildFieldId == field.FieldId)))
                        continue;
                    if (field.FieldType == FieldType.Guid)
                        s.Add($"            {field.Name} = Guid.NewGuid();");
                }
                s.Add($"        }}");
            }

            // tostring override
            if (CurrentEntity.PrimaryFieldId.HasValue)
            {
                s.Add($"");
                s.Add($"        public override string ToString()");
                s.Add($"        {{");
                if (CurrentEntity.PrimaryField.NetType == "string")
                    s.Add($"            return {CurrentEntity.PrimaryField.Name};");
                else
                    s.Add($"            return Convert.ToString({CurrentEntity.PrimaryField.Name});");
                s.Add($"        }}");
            }

            s.Add($"    }}");
            s.Add($"}}");

            return RunCodeReplacements(s.ToString(), CodeType.Model);
        }

        public string GenerateTypeScriptModel()
        {
            //if (CurrentEntity.EntityType == EntityType.User) throw new NotImplementedException("Not Implemented: GenerateModel for User entity");

            var s = new StringBuilder();
            s.Add($"import {{ SearchOptions, PagingOptions }} from './http.model';");
            foreach (var relationship in CurrentEntity.RelationshipsAsChild.Where(r => !r.ParentEntity.Exclude).OrderBy(o => o.ParentEntity.Name))
            {
                s.Add($"import {{ {relationship.ParentEntity.Name} }} from './{ relationship.ParentEntity.Name.ToLower() }.model';");
            }
            s.Add($"");

            s.Add($"export class {CurrentEntity.Name} {{");

            // fields
            foreach (var field in CurrentEntity.Fields.OrderBy(f => f.FieldOrder))
            {
                s.Add($"   {field.Name.ToCamelCase()}: {field.JavascriptType};");
            }
            foreach (var relationship in CurrentEntity.RelationshipsAsChild.Where(r => !r.ParentEntity.Exclude).OrderBy(o => o.ParentEntity.Name))
            {
                s.Add($"   {relationship.ParentName.ToCamelCase()}: {relationship.ParentEntity.Name};");
            }
            s.Add($"");

            s.Add($"   constructor() {{");
            foreach (var field in CurrentEntity.KeyFields.OrderBy(f => f.FieldOrder))
            {
                if (field.CustomType == CustomType.Guid)
                    s.Add($"      this.{field.Name.ToCamelCase()} = \"00000000-0000-0000-0000-000000000000\";");
            }
            s.Add($"   }}");
            s.Add($"}}");
            s.Add($"");

            s.Add($"export class {CurrentEntity.Name}SearchOptions extends SearchOptions {{");

            if (CurrentEntity.Fields.Any(f => f.SearchType == SearchType.Text))
            {
                s.Add($"   q: string;");// = undefined
            }
            foreach (var field in CurrentEntity.Fields.Where(f => f.SearchType == SearchType.Exact).OrderBy(f => f.FieldOrder))
            {
                s.Add($"   {field.Name.ToCamelCase()}: {field.JavascriptType};");
            }

            s.Add($"}}");
            s.Add($"");

            s.Add($"export class {CurrentEntity.Name}SearchResponse {{");
            s.Add($"   {CurrentEntity.PluralName.ToCamelCase()}: {CurrentEntity.Name}[];");
            s.Add($"   headers: PagingOptions;");
            s.Add($"}}");



            return RunCodeReplacements(s.ToString(), CodeType.Model);
        }

        public string GenerateEnums()
        {
            var s = new StringBuilder();

            s.Add($"namespace {CurrentEntity.Project.Namespace}.Models");
            s.Add($"{{");
            foreach (var lookup in Lookups)
            {
                s.Add($"    public enum " + lookup.Name);
                s.Add($"    {{");
                var options = lookup.LookupOptions.OrderBy(o => o.SortOrder);
                foreach (var option in options)
                    s.Add($"        {option.Name}{(option.Value.HasValue ? " = " + option.Value : string.Empty)}" + (option == options.Last() ? string.Empty : ","));
                s.Add($"    }}");
                s.Add($"");
            }
            s.Add($"    public static class Extensions");
            s.Add($"    {{");
            foreach (var lookup in Lookups)
            {
                s.Add($"        public static string Label(this {lookup.Name} {lookup.Name.ToCamelCase()})");
                s.Add($"        {{");
                s.Add($"            switch ({lookup.Name.ToCamelCase()})");
                s.Add($"            {{");
                var options = lookup.LookupOptions.OrderBy(o => o.SortOrder);
                foreach (var option in options)
                {
                    s.Add($"                case {lookup.Name}.{option.Name}:");
                    s.Add($"                    return \"{option.FriendlyName.Replace("\"", "\\\"")}\";");
                }
                s.Add($"                default:");
                s.Add($"                    return null;");
                s.Add($"            }}");
                s.Add($"        }}");
                s.Add($"");
            }
            s.Add($"    }}");
            s.Add($"}}");

            return RunCodeReplacements(s.ToString(), CodeType.Enums);
        }

        public string GenerateSettingsDTO()
        {
            var s = new StringBuilder();

            s.Add($"using System;");
            s.Add($"using System.Collections.Generic;");
            s.Add($"");
            s.Add($"namespace {CurrentEntity.Project.Namespace}.Models");
            s.Add($"{{");
            s.Add($"    public partial class SettingsDTO");
            s.Add($"    {{");
            foreach (var lookup in Lookups)
                s.Add($"        public List<EnumDTO> {lookup.Name} {{ get; set; }}");
            s.Add($"");
            s.Add($"        public SettingsDTO()");
            s.Add($"        {{");
            foreach (var lookup in Lookups)
            {
                s.Add($"            {lookup.Name} = new List<EnumDTO>();");
                s.Add($"            foreach ({lookup.Name} type in Enum.GetValues(typeof({lookup.Name})))");
                s.Add($"                {lookup.Name}.Add(new EnumDTO {{ Id = (int)type, Name = type.ToString(), Label = type.Label() }});");
                s.Add($"");
            }
            s.Add($"        }}");
            s.Add($"    }}");
            s.Add($"}}");

            return RunCodeReplacements(s.ToString(), CodeType.SettingsDTO);
        }

        public string GenerateDTO()
        {
            //if (CurrentEntity.EntityType == EntityType.User) return string.Empty;

            var s = new StringBuilder();

            s.Add($"using System;");
            s.Add($"using System.ComponentModel.DataAnnotations;");
            if (CurrentEntity.EntityType == EntityType.User)
                s.Add($"using System.Collections.Generic;");
            s.Add($"");
            s.Add($"namespace {CurrentEntity.Project.Namespace}.Models");
            s.Add($"{{");
            s.Add($"    public class {CurrentEntity.DTOName}");
            s.Add($"    {{");
            foreach (var field in CurrentEntity.Fields.Where(f => f.EditPageType != EditPageType.Exclude).OrderBy(f => f.FieldOrder))
            {
                var attributes = new List<string>();

                if (field.EditPageType != EditPageType.CalculatedField)
                {
                    if (!field.IsNullable)
                    {
                        // to allow empty strings, can't be null and must use convertemptystringtonull...
                        if (field.CustomType == CustomType.String)
                            attributes.Add("DisplayFormat(ConvertEmptyStringToNull = false)");
                        else if (field.EditPageType != EditPageType.ReadOnly)
                            attributes.Add("Required");
                    }
                    if (field.NetType == "string" && field.Length > 0)
                        attributes.Add($"MaxLength({field.Length}){(field.MinLength > 0 ? $", MinLength({ field.MinLength})" : "")}");
                }

                if (attributes.Any())
                    s.Add($"        [{string.Join(", ", attributes)}]");

                // force nullable for readonly fields
                s.Add($"        public {Field.GetNetType(field.FieldType, field.EditPageType == EditPageType.ReadOnly ? true : field.IsNullable, field.Lookup)} {field.Name} {{ get; set; }}");
                s.Add($"");
            }
            // sort order on relationships is for parents. for childre, just use name
            foreach (var relationship in CurrentEntity.RelationshipsAsChild.OrderBy(r => r.ParentEntity.Name))
            {
                // using exclude to avoid circular references. example: KTU-PACK: version => localisation => contentset => version (UpdateFromVersion)
                if (relationship.RelationshipAncestorLimit == RelationshipAncestorLimits.Exclude) continue;
                if (relationship.RelationshipFields.Count == 1 && relationship.RelationshipFields.First().ChildField.EditPageType == EditPageType.Exclude) continue;
                s.Add($"        public {relationship.ParentEntity.Name}DTO {relationship.ParentName} {{ get; set; }}");
                s.Add($"");
            }
            if (CurrentEntity.EntityType == EntityType.User)
            {
                s.Add($"        public string Email {{ get; set; }}");
                s.Add($"");
                s.Add($"        public IList<Guid> RoleIds {{ get; set; }}");
                s.Add($"");
            }
            s.Add($"    }}");
            s.Add($"");
            s.Add($"    public static partial class ModelFactory");
            s.Add($"    {{");
            s.Add($"        public static {CurrentEntity.DTOName} Create({CurrentEntity.Name} {CurrentEntity.CamelCaseName})");
            s.Add($"        {{");
            s.Add($"            if ({CurrentEntity.CamelCaseName} == null) return null;");
            s.Add($"");
            if (CurrentEntity.EntityType == EntityType.User)
            {
                s.Add($"            var roleIds = new List<Guid>();");
                s.Add($"            foreach (var role in {CurrentEntity.CamelCaseName}.Roles)");
                s.Add($"                roleIds.Add(role.RoleId);");
                s.Add($"");
            }
            s.Add($"            var {CurrentEntity.DTOName.ToCamelCase()} = new {CurrentEntity.DTOName}();");
            s.Add($"");
            foreach (var field in CurrentEntity.Fields.Where(f => f.EditPageType != EditPageType.Exclude).OrderBy(f => f.FieldOrder))
            {
                s.Add($"            {CurrentEntity.DTOName.ToCamelCase()}.{field.Name} = {CurrentEntity.CamelCaseName}.{field.Name};");
            }
            if (CurrentEntity.EntityType == EntityType.User)
            {
                s.Add($"            {CurrentEntity.DTOName.ToCamelCase()}.Email = {CurrentEntity.CamelCaseName}.Email;");
                s.Add($"            {CurrentEntity.DTOName.ToCamelCase()}.RoleIds = roleIds;");
            }
            foreach (var relationship in CurrentEntity.RelationshipsAsChild.OrderBy(o => o.ParentFriendlyName))
            {
                // using exclude to avoid circular references. example: KTU-PACK: version => localisation => contentset => version (UpdateFromVersion)
                if (relationship.RelationshipAncestorLimit == RelationshipAncestorLimits.Exclude) continue;
                if (relationship.RelationshipFields.Count == 1 && relationship.RelationshipFields.First().ChildField.EditPageType == EditPageType.Exclude) continue;
                s.Add($"            {CurrentEntity.DTOName.ToCamelCase()}.{relationship.ParentName} = Create({CurrentEntity.CamelCaseName}.{relationship.ParentName});");
            }
            s.Add($"");
            s.Add($"            return {CurrentEntity.DTOName.ToCamelCase()};");
            s.Add($"        }}");
            s.Add($"");
            s.Add($"        public static void Hydrate({CurrentEntity.Name} {CurrentEntity.CamelCaseName}, {CurrentEntity.DTOName} {CurrentEntity.DTOName.ToCamelCase()})");
            s.Add($"        {{");
            if (CurrentEntity.EntityType == EntityType.User)
            {
                s.Add($"            {CurrentEntity.CamelCaseName}.UserName = {CurrentEntity.DTOName.ToCamelCase()}.Email;");
                s.Add($"            {CurrentEntity.CamelCaseName}.Email = {CurrentEntity.DTOName.ToCamelCase()}.Email;");
            }
            foreach (var field in CurrentEntity.Fields.OrderBy(f => f.FieldOrder))
            {
                if (field.KeyField || field.EditPageType == EditPageType.ReadOnly) continue;
                if (field.EditPageType == EditPageType.Exclude || field.EditPageType == EditPageType.CalculatedField) continue;
                s.Add($"            {CurrentEntity.CamelCaseName}.{field.Name} = {CurrentEntity.DTOName.ToCamelCase()}.{field.Name};");
            }
            s.Add($"        }}");
            s.Add($"    }}");
            s.Add($"}}");

            return RunCodeReplacements(s.ToString(), CodeType.DTO);
        }

        public string GenerateDbContext()
        {
            var calculatedFields = AllEntities.SelectMany(e => e.Fields).Where(f => f.EditPageType == EditPageType.CalculatedField);

            var s = new StringBuilder();

            s.Add($"using Microsoft.EntityFrameworkCore;");
            s.Add($"");
            s.Add($"namespace {CurrentEntity.Project.Namespace}.Models");
            s.Add($"{{");
            s.Add($"    public partial class ApplicationDbContext");
            s.Add($"    {{");
            foreach (var e in NormalEntities)
                s.Add($"        public DbSet<{e.Name}> {e.PluralName} {{ get; set; }}");
            s.Add($"");
            s.Add($"        public void ConfigureModelBuilder(ModelBuilder modelBuilder)");
            s.Add($"        {{");
            //foreach (var field in calculatedFields)
            //{
            //    s.Add($"            modelBuilder.Entity<{field.Entity.Name}>().Ignore(t => t.{field.Name});");
            //}
            //if (calculatedFields.Count() > 0) s.Add($"");

            foreach (var entity in AllEntities.OrderBy(o => o.Name))
            {
                var needsBreak = false;
                if (entity.KeyFields.Count > 1)
                {
                    s.Add($"            modelBuilder.Entity<{entity.Name}>().HasKey(o => new {{ {entity.KeyFields.Select(o => "o." + o.Name).Aggregate((current, next) => current + ", " + next)} }}).HasName(\"PK_{entity.Name}\");");
                }
                foreach (var field in entity.Fields.Where(o => o.IsUnique && !o.IsNullable))
                {
                    s.Add($"            modelBuilder.Entity<{entity.Name}>().HasIndex(o => o.{field.Name}).HasName(\"IX_{entity.Name}_{field.Name}\").IsUnique();");
                    needsBreak = true;
                }
                if (needsBreak) s.Add($"");
            }

            foreach (var entity in AllEntities.OrderBy(o => o.Name))
            {
                foreach (var relationship in entity.RelationshipsAsChild.OrderBy(o => o.SortOrder).ThenBy(o => o.ParentEntity.Name))
                {
                    if (!entity.RelationshipsAsChild.Any(r => r.ParentEntityId == relationship.ParentEntityId && r.RelationshipId != relationship.RelationshipId)) continue;
                    s.Add($"            modelBuilder.Entity<{entity.Name}>()");
                    s.Add($"                .{(relationship.RelationshipFields.First().ChildField.IsNullable ? "HasOptional" : "HasRequired")}(o => o.{relationship.ParentName})");
                    s.Add($"                .WithMany(o => o.{relationship.CollectionName})");
                    s.Add($"                .HasForeignKey(o => o.{relationship.RelationshipFields.First().ChildField.Name});");
                    s.Add($"");
                }
            }

            var smallDateTimeFields = DbContext.Fields.Where(f => f.FieldType == FieldType.SmallDateTime && f.Entity.ProjectId == CurrentEntity.ProjectId).OrderBy(f => f.Entity.Name).ThenBy(f => f.FieldOrder).ToList();
            foreach (var field in smallDateTimeFields.OrderBy(o => o.Entity.Name).ThenBy(o => o.FieldOrder))
            {
                if (field.EditPageType == EditPageType.CalculatedField) continue;
                s.Add($"            modelBuilder.Entity<{field.Entity.Name}>().Property(o => o.{field.Name}).HasColumnType(\"smalldatetime\");");
            }
            s.Add($"        }}");

            if (calculatedFields.Count() > 0)
            {
                s.Add($"");
                s.Add($"        public void AddComputedColumns()");
                s.Add($"        {{");
                foreach (var field in calculatedFields)
                {
                    s.Add($"            CreateComputedColumn(\"{(field.Entity.EntityType == EntityType.User ? "AspNetUsers" : field.Entity.PluralName)}\", \"{field.Name}\", \"{field.CalculatedFieldDefinition.Replace("\"", "'")}\");");
                }
                s.Add($"        }}");
            }
            var nullableUniques = DbContext.Fields.Where(o => o.IsUnique && o.IsNullable && o.Entity.ProjectId == CurrentEntity.ProjectId).ToList();
            if (nullableUniques.Count > 0)
            {
                s.Add($"");
                s.Add($"        public void AddNullableUniqueIndexes()");
                s.Add($"        {{");
                foreach (var field in nullableUniques.OrderBy(o => o.Entity.Name).ThenBy(o => o.Name))
                {
                    s.Add($"            CreateNullableUniqueIndex(\"{field.Entity.Name}\", \"{field.Name}\");");
                }
                s.Add($"        }}");
            }
            s.Add($"    }}");
            s.Add($"}}");

            return RunCodeReplacements(s.ToString(), CodeType.DbContext);
        }

        public string GenerateController()
        {
            //if (CurrentEntity.EntityType == EntityType.User) return string.Empty;

            var s = new StringBuilder();

            s.Add($"using System;");
            s.Add($"using System.Linq;");
            s.Add($"using System.Threading.Tasks;");
            s.Add($"using Microsoft.AspNetCore.Mvc;");
            s.Add($"using Microsoft.AspNetCore.Identity;");
            s.Add($"using Microsoft.EntityFrameworkCore;");
            s.Add($"using {CurrentEntity.Project.Namespace}.Models;");
            s.Add($"using Microsoft.Extensions.Options;");
            s.Add($"");
            s.Add($"namespace {CurrentEntity.Project.Namespace}.Controllers");
            s.Add($"{{");
            // todo: add back roles!
            s.Add($"    [Route(\"api/[Controller]\")]");
            s.Add($"    public {(CurrentEntity.PartialControllerClass ? "partial " : string.Empty)}class {CurrentEntity.PluralName}Controller : BaseApiController");
            s.Add($"    {{");
            if (CurrentEntity.EntityType == EntityType.User)
            {
                s.Add($"        private RoleManager<AppRole> rm;");
                s.Add($"        private IOptions<PasswordOptions> opts;");
                s.Add($"        public UsersController(ApplicationDbContext _db, UserManager<User> _um, RoleManager<AppRole> _rm, IOptions<PasswordOptions> _opts) ");
                s.Add($"            : base(_db, _um) {{ rm = _rm; opts = _opts; }}");
            }
            else
            {
                s.Add($"        public {CurrentEntity.PluralName}Controller(ApplicationDbContext _db, UserManager<User> um) : base(_db, um) {{ }}");
            }
            s.Add($"");

            #region search
            s.Add($"        [HttpGet(\"\")]");

            var fieldsToSearch = new List<Field>();
            foreach (var relationship in CurrentEntity.RelationshipsAsChild.OrderBy(r => r.RelationshipFields.Min(f => f.ChildField.FieldOrder)))
                foreach (var relationshipField in relationship.RelationshipFields)
                    fieldsToSearch.Add(relationshipField.ChildField);
            foreach (var field in CurrentEntity.ExactSearchFields)
                if (!fieldsToSearch.Contains(field))
                    fieldsToSearch.Add(field);
            foreach (var field in CurrentEntity.RangeSearchFields)
                fieldsToSearch.Add(field);

            s.Add($"        public async Task<IActionResult> Search([FromQuery]PagingOptions pagingOptions{(CurrentEntity.TextSearchFields.Count > 0 ? ", [FromQuery]string q = null" : "")}{(fieldsToSearch.Count > 0 ? $", {fieldsToSearch.Select(f => f.ControllerSearchParams).Aggregate((current, next) => current + ", " + next)}" : "") + (CurrentEntity.EntityType == EntityType.User ? ", Guid? roleId = null" : "")})");
            s.Add($"        {{");
            s.Add($"            if (pagingOptions == null) pagingOptions = new PagingOptions();");
            s.Add($"");

            if (CurrentEntity.EntityType == EntityType.User)
            {
                s.Add($"            IQueryable<User> results = userManager.Users;");
                s.Add($"            results = results.Include(o => o.Roles);");
                s.Add($"");
                s.Add($"            if (roleId != null) results = results.Where(o => o.Roles.Any(r => r.RoleId == roleId));");
                s.Add($"");
            }
            else
            {
                s.Add($"            IQueryable<{CurrentEntity.Name}> results = {CurrentEntity.Project.DbContextVariable}.{CurrentEntity.PluralName};");
            }

            if (CurrentEntity.RelationshipsAsChild.Where(r => r.RelationshipAncestorLimit != RelationshipAncestorLimits.Exclude).Any())
            {
                s.Add($"            if (pagingOptions.IncludeEntities)");
                s.Add($"            {{");
                foreach (var relationship in CurrentEntity.RelationshipsAsChild.OrderBy(r => r.SortOrderOnChild).ThenBy(o => o.ParentName))
                {
                    if (relationship.RelationshipAncestorLimit == RelationshipAncestorLimits.Exclude) continue;

                    foreach (var result in GetTopAncestors(new List<string>(), "o", relationship, relationship.RelationshipAncestorLimit, includeIfHierarchy: true))
                        s.Add($"                results = results.Include(o => {result});");
                }
                s.Add($"            }}");
            }

            if (CurrentEntity.TextSearchFields.Count > 0)
            {
                s.Add($"");
                s.Add($"            if (!string.IsNullOrWhiteSpace(q))");
                s.Add($"                results = results.Where(o => {CurrentEntity.TextSearchFields.Select(o => $"o.{o.Name + (o.CustomType == CustomType.String ? string.Empty : ".toString()")}.Contains(q)").Aggregate((current, next) => current + " || " + next) });");
            }

            if (fieldsToSearch.Count > 0)
            {
                s.Add($"");
                foreach (var field in fieldsToSearch)
                {
                    if (field.SearchType == SearchType.Range && field.CustomType == CustomType.Date)
                    {
                        s.Add($"            if (from{field.Name}.HasValue) {{ var from{field.Name}Utc = from{field.Name}.Value.ToUniversalTime(); results = results.Where(o => o.{ field.Name} >= from{field.Name}Utc); }}");
                        s.Add($"            if (to{field.Name}.HasValue) {{ var to{field.Name}Utc = to{field.Name}.Value.ToUniversalTime(); results = results.Where(o => o.{ field.Name} <= to{field.Name}Utc); }}");
                    }
                    else
                    {
                        s.Add($"            if ({field.Name.ToCamelCase()}{(field.CustomType == CustomType.String ? " != null" : ".HasValue")}) results = results.Where(o => o.{field.Name} == {field.Name.ToCamelCase()});");
                    }
                }
            }

            s.Add($"");
            if (CurrentEntity.SortOrderFields.Count > 0)
                s.Add($"            results = results.Order{CurrentEntity.SortOrderFields.Select(f => "By" + (f.SortDescending ? "Descending" : string.Empty) + "(o => o." + f.Name + ")").Aggregate((current, next) => current + ".Then" + next)};");

            //var counter = 0;
            //foreach (var field in CurrentEntity.SortOrderFields)
            //{
            //    s.Add($"            results = results.{(counter == 0 ? "Order" : "Then")}By(o => o.{field.Name});");
            //    counter++;
            //}
            s.Add($"");
            s.Add($"            return Ok((await GetPaginatedResponse(results, pagingOptions)).Select(o => ModelFactory.Create(o)));");
            s.Add($"        }}");
            s.Add($"");
            #endregion

            #region get
            s.Add($"        [HttpGet(\"{CurrentEntity.RoutePath}\")]");
            s.Add($"        public async Task<IActionResult> Get({CurrentEntity.ControllerParameters})");
            s.Add($"        {{");
            if (CurrentEntity.EntityType == EntityType.User)
            {
                s.Add($"            var user = await userManager.Users");
                s.Add($"                .Include(o => o.Roles)");
            }
            else
            {
                s.Add($"            var {CurrentEntity.CamelCaseName} = await {CurrentEntity.Project.DbContextVariable}.{CurrentEntity.PluralName}");
            }
            foreach (var relationship in CurrentEntity.RelationshipsAsChild.OrderBy(r => r.SortOrder).ThenBy(o => o.ParentName))
            {
                if (relationship.RelationshipAncestorLimit == RelationshipAncestorLimits.Exclude) continue;

                //if (relationship.Hierarchy) -- these are needed e.g. when using select-directives, then it needs to have the related entities to show the label (not just the id, which nya-bs-select could make do with)
                foreach (var result in GetTopAncestors(new List<string>(), "o", relationship, relationship.RelationshipAncestorLimit, includeIfHierarchy: true))
                    s.Add($"                .Include(o => {result})");
            }
            s.Add($"                .SingleOrDefaultAsync(o => {GetKeyFieldLinq("o")});");
            s.Add($"");
            s.Add($"            if ({CurrentEntity.CamelCaseName} == null)");
            s.Add($"                return NotFound();");
            s.Add($"");
            s.Add($"            return Ok(ModelFactory.Create({CurrentEntity.CamelCaseName}));");
            s.Add($"        }}");
            s.Add($"");
            #endregion

            #region save
            s.Add($"        [HttpPost(\"{CurrentEntity.RoutePath}\"){(CurrentEntity.AuthorizationType == AuthorizationType.ProtectChanges ? (CurrentEntity.Project.UseStringAuthorizeAttributes ? ", Authorize(Roles = \"Administrator\")" : ", AuthorizeRoles(Roles.Administrator)") : string.Empty)}]");
            s.Add($"        public async Task<IActionResult> Save({CurrentEntity.ControllerParameters}, [FromBody]{CurrentEntity.DTOName} {CurrentEntity.DTOName.ToCamelCase()})");
            s.Add($"        {{");
            s.Add($"            if (!ModelState.IsValid) return BadRequest(ModelState);");
            s.Add($"");
            s.Add($"            if ({GetKeyFieldLinq(CurrentEntity.DTOName.ToCamelCase(), null, "!=", "||")}) return BadRequest(\"Id mismatch\");");
            s.Add($"");
            foreach (var field in CurrentEntity.Fields.Where(f => f.IsUnique))
            {
                if (field.EditPageType == EditPageType.ReadOnly) continue;

                string hierarchyFields = string.Empty;
                if (ParentHierarchyRelationship != null)
                {
                    foreach (var relField in ParentHierarchyRelationship.RelationshipFields)
                        hierarchyFields += (hierarchyFields == string.Empty ? "" : " && ") + "o." + relField.ChildField.Name + " == " + CurrentEntity.DTOName.ToCamelCase() + "." + relField.ChildField.Name;
                    hierarchyFields += " && ";
                }
                s.Add($"            if (" + (field.IsNullable ? field.NotNullCheck(CurrentEntity.DTOName.ToCamelCase() + "." + field.Name) + " && " : "") + $"await {CurrentEntity.Project.DbContextVariable}.{CurrentEntity.PluralName}.AnyAsync(o => {hierarchyFields}o.{field.Name} == {CurrentEntity.DTOName.ToCamelCase()}.{field.Name} && {GetKeyFieldLinq("o", CurrentEntity.DTOName.ToCamelCase(), " != ", " || ", true)}))");
                s.Add($"                return BadRequest(\"{field.Label} already exists{(ParentHierarchyRelationship == null ? string.Empty : " on this " + ParentHierarchyRelationship.ParentEntity.FriendlyName)}.\");");
                s.Add($"");
            }
            if (CurrentEntity.HasCompositePrimaryKey)
            {
                // composite keys don't use the insert method, they use the update for both inserts & updates
                s.Add($"            var {CurrentEntity.CamelCaseName} = await {CurrentEntity.Project.DbContextVariable}.{CurrentEntity.PluralName}.SingleOrDefaultAsync(o => {GetKeyFieldLinq("o", CurrentEntity.DTOName.ToCamelCase())});");
                s.Add($"            var isNew = {CurrentEntity.CamelCaseName} == null;");
                s.Add($"");
                s.Add($"            if (isNew)");
                s.Add($"            {{");
                s.Add($"                {CurrentEntity.CamelCaseName} = new {CurrentEntity.Name}();");
                s.Add($"");
                foreach (var field in CurrentEntity.Fields.Where(f => f.KeyField && f.Entity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(rf => rf.ChildFieldId == f.FieldId))))
                {
                    s.Add($"                {CurrentEntity.CamelCaseName}.{field.Name} = {CurrentEntity.DTOName.ToCamelCase() + "." + field.Name};");
                }
                foreach (var field in CurrentEntity.Fields.Where(f => !string.IsNullOrWhiteSpace(f.ControllerInsertOverride)))
                {
                    s.Add($"                {CurrentEntity.CamelCaseName}.{field.Name} = {field.ControllerInsertOverride};");
                }
                if (CurrentEntity.Fields.Any(f => !string.IsNullOrWhiteSpace(f.ControllerInsertOverride)) || CurrentEntity.Fields.Any(f => f.KeyField && f.Entity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(rf => rf.ChildFieldId == f.FieldId))))
                    s.Add($"");
                if (CurrentEntity.HasASortField)
                {
                    var field = CurrentEntity.SortField;
                    var sort = $"                {CurrentEntity.DTOName.ToCamelCase()}.{field.Name} = (await {CurrentEntity.Project.DbContextVariable}.{CurrentEntity.PluralName}";
                    if (CurrentEntity.RelationshipsAsChild.Any(r => r.Hierarchy) && CurrentEntity.RelationshipsAsChild.Count(r => r.Hierarchy) == 1)
                    {
                        sort += ".Where(o => " + (CurrentEntity.RelationshipsAsChild.Single(r => r.Hierarchy).RelationshipFields.Select(o => $"o.{o.ChildField.Name} == {CurrentEntity.DTOName.ToCamelCase()}.{o.ChildField.Name}").Aggregate((current, next) => current + " && " + next)) + ")";
                    }
                    sort += $".MaxAsync(o => (int?)o.{field.Name}) ?? -1) + 1;";
                    s.Add(sort);
                    s.Add($"");
                }
                s.Add($"                {CurrentEntity.Project.DbContextVariable}.Entry({CurrentEntity.CamelCaseName}).State = EntityState.Added;");
                s.Add($"            }}");
                s.Add($"            else");
                s.Add($"            {{");
                foreach (var field in CurrentEntity.Fields.Where(f => !string.IsNullOrWhiteSpace(f.ControllerUpdateOverride)).OrderBy(f => f.FieldOrder))
                {
                    s.Add($"                {CurrentEntity.CamelCaseName}.{field.Name} = {field.ControllerUpdateOverride};");
                }
                if (CurrentEntity.Fields.Any(f => !string.IsNullOrWhiteSpace(f.ControllerUpdateOverride)))
                    s.Add($"");
                s.Add($"                {CurrentEntity.Project.DbContextVariable}.Entry({CurrentEntity.CamelCaseName}).State = EntityState.Modified;");
                s.Add($"            }}");
            }
            else
            {
                if (CurrentEntity.EntityType == EntityType.User)
                {
                    s.Add($"            var password = string.Empty;");
                    s.Add($"            if (await db.Users.AnyAsync(o => o.Email == userDTO.Email && o.Id != userDTO.Id))");
                    s.Add($"                return BadRequest(\"Email already exists.\");");
                    s.Add($"");
                }
                s.Add($"            var isNew = {CurrentEntity.KeyFields.Select(f => CurrentEntity.DTOName.ToCamelCase() + "." + f.Name + " == " + f.EmptyValue).Aggregate((current, next) => current + " && " + next)};");
                s.Add($"");
                s.Add($"            {CurrentEntity.Name} {CurrentEntity.CamelCaseName};");
                s.Add($"            if (isNew)");
                s.Add($"            {{");
                s.Add($"                {CurrentEntity.CamelCaseName} = new {CurrentEntity.Name}();");
                if (CurrentEntity.EntityType == EntityType.User)
                    s.Add($"                password = Utilities.GenerateRandomPassword(opts.Value);");
                s.Add($"");
                foreach (var field in CurrentEntity.Fields.Where(f => !string.IsNullOrWhiteSpace(f.ControllerInsertOverride)))
                {
                    s.Add($"                {CurrentEntity.CamelCaseName}.{field.Name} = {field.ControllerInsertOverride};");
                }
                if (CurrentEntity.Fields.Any(f => !string.IsNullOrWhiteSpace(f.ControllerInsertOverride)))
                    s.Add($"");
                if (CurrentEntity.HasASortField)
                {
                    var field = CurrentEntity.SortField;
                    var sort = $"                {CurrentEntity.DTOName.ToCamelCase()}.{field.Name} = (await {CurrentEntity.Project.DbContextVariable}.{CurrentEntity.PluralName}";
                    if (CurrentEntity.RelationshipsAsChild.Any(r => r.Hierarchy) && CurrentEntity.RelationshipsAsChild.Count(r => r.Hierarchy) == 1)
                    {
                        sort += ".Where(o => " + (CurrentEntity.RelationshipsAsChild.Single(r => r.Hierarchy).RelationshipFields.Select(o => $"o.{o.ChildField.Name} == {CurrentEntity.DTOName.ToCamelCase()}.{o.ChildField.Name}").Aggregate((current, next) => current + " && " + next)) + ")";
                    }
                    sort += $".MaxAsync(o => (int?)o.{field.Name}) ?? 0) + 1;";
                    s.Add(sort);
                    s.Add($"");
                }
                s.Add($"                {CurrentEntity.Project.DbContextVariable}.Entry({CurrentEntity.CamelCaseName}).State = EntityState.Added;");
                s.Add($"            }}");
                s.Add($"            else");
                s.Add($"            {{");
                if (CurrentEntity.EntityType == EntityType.User)
                {
                    s.Add($"                user = await userManager.Users");
                    s.Add($"                    .Include(o => o.Roles)");
                    s.Add($"                    .SingleOrDefaultAsync(o => o.Id == userDTO.Id);");
                }
                else
                {
                    s.Add($"                {CurrentEntity.CamelCaseName} = await {CurrentEntity.Project.DbContextVariable}.{CurrentEntity.PluralName}.SingleOrDefaultAsync(o => {GetKeyFieldLinq("o", CurrentEntity.DTOName.ToCamelCase())});");
                }
                s.Add($"");
                s.Add($"                if ({CurrentEntity.CamelCaseName} == null)");
                s.Add($"                    return NotFound();");
                s.Add($"");
                foreach (var field in CurrentEntity.Fields.Where(f => !string.IsNullOrWhiteSpace(f.ControllerUpdateOverride)))
                {
                    s.Add($"                {CurrentEntity.CamelCaseName}.{field.Name} = {field.ControllerUpdateOverride};");
                }
                if (CurrentEntity.Fields.Any(f => !string.IsNullOrWhiteSpace(f.ControllerUpdateOverride)))
                    s.Add($"");
                s.Add($"                {CurrentEntity.Project.DbContextVariable}.Entry({CurrentEntity.CamelCaseName}).State = EntityState.Modified;");
                s.Add($"            }}");
            }
            s.Add($"");
            s.Add($"            ModelFactory.Hydrate({CurrentEntity.CamelCaseName}, {CurrentEntity.DTOName.ToCamelCase()});");
            s.Add($"");
            if (CurrentEntity.EntityType == EntityType.User)
            {
                s.Add($"            var saveResult = (isNew ? await userManager.CreateAsync(user, password) : await userManager.UpdateAsync(user));");
                s.Add($"");
                s.Add($"            if (!saveResult.Succeeded)");
                s.Add($"                return GetErrorResult(saveResult);");
                s.Add($"");
                s.Add($"            var appRoles = await rm.Roles.ToListAsync();");
                s.Add($"");
                s.Add($"            if (!isNew)");
                s.Add($"            {{");
                s.Add($"                foreach (var roleId in user.Roles.ToList())");
                s.Add($"                {{");
                s.Add($"                    var role = rm.Roles.Single(o => o.Id == roleId.RoleId);");
                s.Add($"                    await userManager.RemoveFromRoleAsync(user, role.Name);");
                s.Add($"                }}");
                s.Add($"            }}");
                s.Add($"");
                s.Add($"            if (userDTO.RoleIds != null)");
                s.Add($"            {{");
                s.Add($"                foreach (var roleId in userDTO.RoleIds)");
                s.Add($"                {{");
                s.Add($"                    var appRole = appRoles.SingleOrDefault(r => r.Id == roleId);");
                s.Add($"                    if (appRole != null)");
                s.Add($"                        await userManager.AddToRoleAsync(user, appRole.Name);");
                s.Add($"                }}");
                s.Add($"            }}");
                s.Add($"");
                s.Add($"            Utilities.SendWelcomeMail(user, password);");
            }
            else
            {
                s.Add($"            await {CurrentEntity.Project.DbContextVariable}.SaveChangesAsync();");
            }
            s.Add($"");
            s.Add($"            return await Get({CurrentEntity.KeyFields.Select(f => CurrentEntity.CamelCaseName + "." + f.Name).Aggregate((current, next) => current + ", " + next)});");
            s.Add($"        }}");
            s.Add($"");
            #endregion

            #region delete
            s.Add($"        [HttpDelete(\"{CurrentEntity.RoutePath}\"){(CurrentEntity.AuthorizationType == AuthorizationType.ProtectChanges ? (CurrentEntity.Project.UseStringAuthorizeAttributes ? ", Authorize(Roles = \"Administrator\")" : ", AuthorizeRoles(Roles.Administrator)") : string.Empty)}]");
            s.Add($"        public async Task<IActionResult> Delete({CurrentEntity.ControllerParameters})");
            s.Add($"        {{");
            s.Add($"            var {CurrentEntity.CamelCaseName} = await {(CurrentEntity.EntityType == EntityType.User ? "userManager" : CurrentEntity.Project.DbContextVariable)}.{CurrentEntity.PluralName}.SingleOrDefaultAsync(o => {GetKeyFieldLinq("o")});");
            s.Add($"");
            s.Add($"            if ({CurrentEntity.CamelCaseName} == null)");
            s.Add($"                return NotFound();");
            s.Add($"");
            foreach (var relationship in CurrentEntity.RelationshipsAsParent.Where(rel => !rel.ChildEntity.Exclude).OrderBy(o => o.SortOrder))
            {
                if (relationship.CascadeDelete)
                {
                    s.Add($"            foreach (var {relationship.ChildEntity.CamelCaseName} in {CurrentEntity.Project.DbContextVariable}.{relationship.ChildEntity.PluralName}.Where(o => {relationship.RelationshipFields.Select(rf => "o." + rf.ChildField.Name + " == " + CurrentEntity.CamelCaseName + "." + rf.ParentField.Name).Aggregate((current, next) => current + " && " + next)}))");
                    s.Add($"                {CurrentEntity.Project.DbContextVariable}.Entry({relationship.ChildEntity.CamelCaseName}).State = EntityState.Deleted;");
                    s.Add($"");
                }
                else
                {
                    var joins = relationship.RelationshipFields.Select(o => $"o.{o.ChildField.Name} == {CurrentEntity.CamelCaseName}.{o.ParentField.Name}").Aggregate((current, next) => current + " && " + next);
                    s.Add($"            if (await {CurrentEntity.Project.DbContextVariable}.{(relationship.ChildEntity.EntityType == EntityType.User ? "Users" : relationship.ChildEntity.PluralName)}.AnyAsync(o => {joins}))");
                    s.Add($"                return BadRequest(\"Unable to delete the {CurrentEntity.FriendlyName.ToLower()} as it has related {relationship.ChildEntity.PluralFriendlyName.ToLower()}\");");
                    s.Add($"");
                }
            }
            // need to add fk checks here!
            if (CurrentEntity.EntityType == EntityType.User)
            {
                s.Add($"            await userManager.DeleteAsync(user);");
            }
            else
            {
                s.Add($"            {CurrentEntity.Project.DbContextVariable}.Entry({CurrentEntity.CamelCaseName}).State = EntityState.Deleted;");
                s.Add($"");
                s.Add($"            await {CurrentEntity.Project.DbContextVariable}.SaveChangesAsync();");
            }
            s.Add($"");
            s.Add($"            return Ok();");
            s.Add($"        }}");
            s.Add($"");
            #endregion

            #region sort
            if (CurrentEntity.HasASortField)
            {
                s.Add($"        [HttpPost(\"sort\"){(CurrentEntity.AuthorizationType == AuthorizationType.ProtectChanges ? (CurrentEntity.Project.UseStringAuthorizeAttributes ? ", Authorize(Roles = \"Administrator\"), " : ", AuthorizeRoles(Roles.Administrator)") : "")}]");
                s.Add($"        public async Task<IActionResult> Sort([FromBody]Guid[] sortedIds)");
                s.Add($"        {{");
                // if it's a child entity, just sort the id's that were sent
                if (CurrentEntity.RelationshipsAsChild.Any(r => r.Hierarchy))
                    s.Add($"            var {CurrentEntity.PluralName.ToCamelCase()} = await {CurrentEntity.Project.DbContextVariable}.{CurrentEntity.PluralName}.Where(o => sortedIds.Contains(o.{CurrentEntity.KeyFields[0].Name})).ToListAsync();");
                else
                    s.Add($"            var {CurrentEntity.PluralName.ToCamelCase()} = await {CurrentEntity.Project.DbContextVariable}.{CurrentEntity.PluralName}.ToListAsync();");
                s.Add($"            if ({CurrentEntity.PluralName.ToCamelCase()}.Count != sortedIds.Length) return BadRequest(\"Some of the {CurrentEntity.PluralFriendlyName.ToLower()} could not be found\");");
                s.Add($"");
                //s.Add($"            var sortOrder = 0;");
                s.Add($"            foreach (var {CurrentEntity.Name.ToCamelCase()} in {CurrentEntity.PluralName.ToCamelCase()})");
                s.Add($"            {{");
                s.Add($"                {CurrentEntity.Project.DbContextVariable}.Entry({CurrentEntity.Name.ToCamelCase()}).State = EntityState.Modified;");
                s.Add($"                {CurrentEntity.Name.ToCamelCase()}.{CurrentEntity.SortField.Name} = {(CurrentEntity.SortField.SortDescending ? "sortedIds.Length - " : "")}Array.IndexOf(sortedIds, {CurrentEntity.Name.ToCamelCase()}.{CurrentEntity.KeyFields[0].Name});");
                //s.Add($"                sortOrder++;");
                s.Add($"            }}");
                s.Add($"");
                s.Add($"            await {CurrentEntity.Project.DbContextVariable}.SaveChangesAsync();");
                s.Add($"");
                s.Add($"            return Ok();");
                s.Add($"        }}");
                s.Add($"");
            }
            #endregion

            #region multiselect saves
            foreach (var rel in CurrentEntity.RelationshipsAsParent.Where(o => o.UseMultiSelect))
            {
                var relationshipField = rel.RelationshipFields.Single();
                var reverseRel = rel.ChildEntity.RelationshipsAsChild.Where(o => o.RelationshipId != rel.RelationshipId).SingleOrDefault();
                var reverseRelationshipField = reverseRel.RelationshipFields.Single();

                s.Add($"        [HttpPost(\"{CurrentEntity.RoutePath}/{rel.ChildEntity.PluralName.ToLower()}\"){(CurrentEntity.AuthorizationType == AuthorizationType.ProtectChanges ? (CurrentEntity.Project.UseStringAuthorizeAttributes ? ", Authorize(Roles = \"Administrator\")" : ", AuthorizeRoles(Roles.Administrator)") : "")}]");
                s.Add($"        public async Task<IActionResult> Save{rel.ChildEntity.Name}({CurrentEntity.ControllerParameters}, [FromBody]{rel.ChildEntity.DTOName}[] {rel.ChildEntity.DTOName.ToCamelCase()}s)");
                s.Add($"        {{");
                s.Add($"            if (!ModelState.IsValid) return BadRequest(ModelState);");
                s.Add($"");
                s.Add($"            foreach (var {rel.ChildEntity.DTOName.ToCamelCase()} in {rel.ChildEntity.DTOName.ToCamelCase()}s)");
                s.Add($"            {{");
                s.Add($"                if ({rel.ChildEntity.DTOName.ToCamelCase()}.{rel.ChildEntity.KeyFields[0].Name} != Guid.Empty) return BadRequest(\"Invalid {rel.ChildEntity.KeyFields[0].Name}\");");
                s.Add($"");
                s.Add($"                if ({rel.ParentEntity.KeyFields[0].Name.ToCamelCase()} != {rel.ChildEntity.DTOName.ToCamelCase()}.{relationshipField.ChildField.Name}) return BadRequest(\"{CurrentEntity.FriendlyName} ID mismatch\");");
                s.Add($"");
                s.Add($"                if (!await db.{rel.ChildEntity.PluralName}.AnyAsync(o => o.{relationshipField.ChildField} == {rel.ParentEntity.KeyFields[0].Name.ToCamelCase()} && o.{reverseRelationshipField.ParentField} == {rel.ChildEntity.DTOName.ToCamelCase()}.{reverseRelationshipField.ParentField}))");
                s.Add($"                {{");
                s.Add($"                    var {rel.ChildEntity.Name.ToCamelCase()} = new {rel.ChildEntity.Name}();");
                s.Add($"");
                s.Add($"                    db.Entry({rel.ChildEntity.Name.ToCamelCase()}).State = EntityState.Added;");
                s.Add($"");
                s.Add($"                    ModelFactory.Hydrate({rel.ChildEntity.Name.ToCamelCase()}, {rel.ChildEntity.DTOName.ToCamelCase()});");
                s.Add($"                }}");
                s.Add($"            }}");
                s.Add($"");
                s.Add($"            await db.SaveChangesAsync();");
                s.Add($"");
                s.Add($"            return Ok();");
                s.Add($"        }}");
                s.Add($"");
            }
            #endregion

            s.Add($"    }}");
            s.Add($"}}");

            return RunCodeReplacements(s.ToString(), CodeType.Controller);
        }

        public string GenerateBundleConfig()
        {
            var s = new StringBuilder();

            s.Add($"import {{ NgModule }} from '@angular/core';");
            s.Add($"import {{ CommonModule }} from '@angular/common';");
            s.Add($"import {{ FormsModule }} from '@angular/forms';");
            s.Add($"import {{ RouterModule }} from '@angular/router';");
            s.Add($"import {{ PagerComponent }} from './common/pager.component';");
            s.Add($"import {{ NgbModule }} from '@ng-bootstrap/ng-bootstrap';");

            var entitiesToBundle = AllEntities.Where(e => !e.Exclude);
            var componentList = "";
            foreach (var e in entitiesToBundle)
            {
                s.Add($"import {{ {e.Name}ListComponent }} from './{e.PluralName.ToLower()}/{e.Name.ToLower()}.list.component';");
                s.Add($"import {{ {e.Name}EditComponent }} from './{e.PluralName.ToLower()}/{e.Name.ToLower()}.edit.component';");
                componentList += (componentList == "" ? "" : ", ") + $"{e.Name}ListComponent, {e.Name}EditComponent";

                if (e.PreventAppSelectTypeScriptDeployment == null)
                {
                    s.Add($"import {{ {e.Name}SelectComponent }} from './{e.PluralName.ToLower()}/{e.Name.ToLower()}.select.component';");
                    componentList += (componentList == "" ? "" : ", ") + $"{e.Name}SelectComponent";
                }

                if (e.PreventSelectModalTypeScriptDeployment == null)
                {
                    s.Add($"import {{ {e.Name}ModalComponent }} from './{e.PluralName.ToLower()}/{e.Name.ToLower()}.modal.component';");
                    componentList += (componentList == "" ? "" : ", ") + $"{e.Name}ModalComponent";
                }
            }
            s.Add($"import {{ GeneratedRoutes }} from './generated.routes';");
            s.Add($"");


            s.Add($"@NgModule({{");
            s.Add($"   declarations: [PagerComponent, {componentList}],");
            s.Add($"   imports: [");
            s.Add($"      CommonModule,");
            s.Add($"      FormsModule,");
            s.Add($"      RouterModule.forChild(GeneratedRoutes),");
            s.Add($"      NgbModule");
            s.Add($"   ]");
            s.Add($"}})");
            s.Add($"export class GeneratedModule {{ }}");

            return RunCodeReplacements(s.ToString(), CodeType.BundleConfig);

        }

        public string GenerateAppRouter()
        {
            var s = new StringBuilder();

            s.Add($"import {{ Route }} from '@angular/router';");
            s.Add($"import {{ HomeComponent }} from './home/home.component';");
            s.Add($"import {{ AccessGuard }} from './common/auth/accessguard';");
            s.Add($"import {{ MainComponent }} from './main.component';");

            var allEntities = AllEntities.Where(e => !e.Exclude).OrderBy(o => o.Name);
            foreach (var e in allEntities)
            {
                s.Add($"import {{ {e.Name}ListComponent }} from './{e.PluralName.ToLower()}/{e.Name.ToLower()}.list.component';");
                s.Add($"import {{ {e.Name}EditComponent }} from './{e.PluralName.ToLower()}/{e.Name.ToLower()}.edit.component';");
            }
            s.Add($"");

            s.Add($"export const GeneratedRoutes: Route[] = [");
            s.Add($"   {{");
            s.Add($"      path: '',");
            s.Add($"      component: MainComponent,");
            s.Add($"      data: {{}},");
            s.Add($"      children: [");
            s.Add($"         {{");
            s.Add($"            path: '',");
            s.Add($"            canActivate: [AccessGuard],");
            s.Add($"            canActivateChild: [AccessGuard],");
            s.Add($"            component: HomeComponent,");
            s.Add($"            pathMatch: 'full',");
            s.Add($"            data: {{ breadcrumb: 'Home' }},");
            s.Add($"         }},");

            foreach (var e in allEntities.OrderBy(o => o.Name))
            {
                var hasChildren = !e.RelationshipsAsChild.Any(r => r.Hierarchy);

                s.Add($"         {{");
                s.Add($"            path: '{e.PluralName.ToLower()}',");
                s.Add($"            canActivate: [AccessGuard],");
                s.Add($"            canActivateChild: [AccessGuard],");
                s.Add($"            component: {e.Name}ListComponent,");
                s.Add($"            data: {{ breadcrumb: '{e.PluralFriendlyName}' }}" + (hasChildren ? "," : ""));
                if (hasChildren)
                {
                    s.Add($"            children: [");
                    WriteEditRoute(new List<Entity> { e }, s, 0);
                    s.Add($"            ]");
                }
                s.Add($"         }}" + (e == allEntities.Last() ? "" : ","));
            }

            s.Add($"      ]");
            s.Add($"   }}");
            s.Add($"];");

            return RunCodeReplacements(s.ToString(), CodeType.AppRouter);

        }

        private void WriteEditRoute(IEnumerable<Entity> entities, StringBuilder s, int level)
        {
            var tabs = String.Concat(Enumerable.Repeat("      ", level));

            foreach (var entity in entities.OrderBy(o => o.Name))
            {
                var childEntities = entity.RelationshipsAsParent.Where(r => r.Hierarchy).Select(o => o.ChildEntity);

                s.Add(tabs + $"               {{");
                s.Add(tabs + $"                  path: '{(level == 0 ? "" : entity.PluralName.ToLower() + "/")}{entity.KeyFields.Select(o => ":" + o.Name.ToCamelCase()).Aggregate((current, next) => { return current + "/" + next; })}',");
                s.Add(tabs + $"                  component: {entity.Name}EditComponent,");
                s.Add(tabs + $"                  canActivate: [AccessGuard],");
                s.Add(tabs + $"                  canActivateChild: [AccessGuard],");
                s.Add(tabs + $"                  data: {{");
                s.Add(tabs + $"                     breadcrumb: 'Add {entity.FriendlyName}'");
                s.Add(tabs + $"                  }}" + (childEntities.Any() ? "," : ""));
                if (childEntities.Any())
                {
                    s.Add(tabs + $"                  children: [");
                    WriteEditRoute(childEntities, s, level + 1);
                    s.Add(tabs + $"                  ]");
                }

                s.Add(tabs + $"               }}" + (entity == entities.Last() ? "" : ","));
            }
        }

        public string GenerateApiResource()
        {
            var s = new StringBuilder();

            var noKeysEntity = NormalEntities.FirstOrDefault(e => e.KeyFields.Count == 0);
            if (noKeysEntity != null)
                throw new InvalidOperationException(noKeysEntity.FriendlyName + " has no keys defined");

            s.Add($"import {{ environment }} from '../../../environments/environment';");
            s.Add($"import {{ Injectable }} from '@angular/core';");
            s.Add($"import {{ HttpClient, HttpParams }} from '@angular/common/http';");
            s.Add($"import {{ Observable }} from 'rxjs';");
            s.Add($"import {{ map }} from 'rxjs/operators';");
            s.Add($"import {{ {CurrentEntity.Name}, {CurrentEntity.Name}SearchOptions, {CurrentEntity.Name}SearchResponse }} from '../models/{CurrentEntity.Name.ToLower()}.model';");
            s.Add($"import {{ SearchQuery, PagingOptions }} from '../models/http.model';");
            s.Add($"");
            s.Add($"@Injectable({{ providedIn: 'root' }})");
            s.Add($"export class {CurrentEntity.Name}Service extends SearchQuery {{");
            s.Add($"");

            s.Add($"   constructor(private http: HttpClient) {{");
            s.Add($"      super();");
            s.Add($"   }}");
            s.Add($"");

            s.Add($"   search(params: {CurrentEntity.Name}SearchOptions): Observable<{CurrentEntity.Name}SearchResponse> {{");
            s.Add($"      const queryParams: HttpParams = this.buildQueryParams(params);");
            s.Add($"      return this.http.get(`${{environment.baseApiUrl}}{CurrentEntity.PluralName.ToLower()}`, {{ params: queryParams, observe: 'response' }})");
            s.Add($"         .pipe(");
            s.Add($"            map(response => {{");
            s.Add($"               const headers = <PagingOptions>JSON.parse(response.headers.get(\"x-pagination\"))");
            s.Add($"               const {CurrentEntity.PluralName.ToCamelCase()} = <{CurrentEntity.Name}[]>response.body;");
            s.Add($"               return {{ {CurrentEntity.PluralName.ToCamelCase()}: {CurrentEntity.PluralName.ToCamelCase()}, headers: headers }};");
            s.Add($"            }})");
            s.Add($"         );");
            s.Add($"   }}");
            s.Add($"");

            var getParams = CurrentEntity.KeyFields.Select(o => o.Name.ToCamelCase() + ": " + o.JavascriptType).Aggregate((current, next) => current + ", " + next);
            var saveParams = CurrentEntity.Name.ToCamelCase() + ": " + CurrentEntity.Name;
            var getUrl = CurrentEntity.KeyFields.Select(o => " + " + o.Name.ToCamelCase()).Aggregate((current, next) => current + " + " + next);
            var saveUrl = CurrentEntity.KeyFields.Select(o => " + " + CurrentEntity.Name.ToCamelCase() + "." + o.Name.ToCamelCase()).Aggregate((current, next) => current + " + " + next);

            s.Add($"   get({getParams}): Observable<{CurrentEntity.Name}> {{");
            s.Add($"      return this.http.get<{CurrentEntity.Name}>(`${{environment.baseApiUrl}}{CurrentEntity.PluralName.ToLower()}/`{getUrl});");
            s.Add($"   }}");
            s.Add($"");

            s.Add($"   save({saveParams}): Observable<{CurrentEntity.Name}> {{");
            s.Add($"      return this.http.post<{CurrentEntity.Name}>(`${{environment.baseApiUrl}}{CurrentEntity.PluralName.ToLower()}/`{saveUrl}, {CurrentEntity.Name.ToCamelCase()});");
            s.Add($"   }}");
            s.Add($"");

            s.Add($"   delete({getParams}): Observable<void> {{");
            s.Add($"      return this.http.delete<void>(`${{environment.baseApiUrl}}{CurrentEntity.PluralName.ToLower()}/`{getUrl});");
            s.Add($"   }}");
            s.Add($"");

            s.Add($"}}");

            return RunCodeReplacements(s.ToString(), CodeType.ApiResource);

        }

        public string GenerateListHtml()
        {
            var s = new StringBuilder();
            s.Add($"<div *ngIf=\"route.children.length === 0\">");
            if (CurrentEntity.Fields.Any(f => f.SearchType != SearchType.None))
            {
                s.Add($"");
                s.Add($"    <form (submit)=\"runSearch(0)\" novalidate>");
                s.Add($"");
                s.Add($"        <div class=\"row\">");
                s.Add($"");
                if (CurrentEntity.Fields.Any(f => f.SearchType == SearchType.Text))
                {
                    s.Add($"            <div class=\"col-sm-6 col-md-4 col-lg-3\">");
                    s.Add($"                <div class=\"form-group\">");
                    s.Add($"                    <input type=\"search\" name=\"q\" id=\"q\" [(ngModel)]=\"searchOptions.q\" max=\"100\" class=\"form-control\" placeholder=\"Search {CurrentEntity.PluralFriendlyName.ToLower()}\" />");
                    s.Add($"                </div>");
                    s.Add($"            </div>");
                    s.Add($"");
                }
                foreach (var field in CurrentEntity.Fields.Where(f => f.SearchType == SearchType.Exact).OrderBy(f => f.FieldOrder))
                {
                    if (field.CustomType == CustomType.Enum)
                    {
                        //s.Add($"            <div class=\"col-sm-6 col-md-4 col-lg-3\">");
                        //s.Add($"                <div class=\"form-group\">");
                        //s.Add($"                    <ol id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" title=\"{field.Label}\" class=\"nya-bs-select form-control\" [(ngModel)]=\"searchOptions.{field.Name.ToCamelCase()}\" data-live-search=\"true\" data-size=\"10\">");
                        //s.Add($"                        <li nya-bs-option=\"item in vm.appSettings.{field.Lookup.Name.ToCamelCase()}\" class=\"nya-bs-option{(CurrentEntity.Project.Bootstrap3 ? "" : " dropdown-item")}\" data-value=\"item.id\">");
                        //s.Add($"                            <a>{{{{item.label}}}}<span class=\"fas fa-check check-mark\"></span></a>");
                        //s.Add($"                        </li>");
                        //s.Add($"                    </ol>");
                        //s.Add($"                </div>");
                        //s.Add($"            </div>");
                        //s.Add($"");
                    }
                    else if (CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(f => f.ChildFieldId == field.FieldId)))
                    {
                        var relationship = CurrentEntity.GetParentSearchRelationship(field);
                        var parentEntity = relationship.ParentEntity;
                        var relField = relationship.RelationshipFields.Single();
                        if (true || relationship.UseSelectorDirective)
                        {
                            s.Add($"            <div class=\"col-sm-6 col-md-4 col-lg-3\">");
                            s.Add($"                <div class=\"form-group\">");
                            s.Add($"                    {relationship.AppSelector}");
                            s.Add($"                </div>");
                            s.Add($"            </div>");
                            s.Add($"");
                        }
                        else
                        {
                            //s.Add($"            <div class=\"col-sm-6 col-md-4 col-lg-3\">");
                            //s.Add($"                <div class=\"form-group\">");
                            //s.Add($"                    <ol id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" title=\"{parentEntity.PluralFriendlyName}\" class=\"nya-bs-select form-control\" [(ngModel)]=\"searchOptions.{field.Name.ToCamelCase()}\" data-live-search=\"true\" data-size=\"10\">");
                            //s.Add($"                        <li nya-bs-option=\"{parentEntity.Name.ToCamelCase()} in vm.{parentEntity.PluralName.ToCamelCase()}\" class=\"nya-bs-option{(CurrentEntity.Project.Bootstrap3 ? "" : " dropdown-item")}\" data-value=\"{parentEntity.Name.ToCamelCase()}.{relField.ParentField.Name.ToCamelCase()}\">");
                            //s.Add($"                            <a>{{{{{parentEntity.Name.ToCamelCase()}.{relationship.ParentField.Name.ToCamelCase()}}}}}<span class=\"fas fa-check check-mark\"></span></a>");
                            //s.Add($"                        </li>");
                            //s.Add($"                    </ol>");
                            //s.Add($"                </div>");
                            //s.Add($"            </div>");
                            //s.Add($"");
                        }
                    }
                }
                foreach (var field in CurrentEntity.Fields.Where(f => f.SearchType == SearchType.Range))
                {
                    if (field.CustomType == CustomType.Date)
                    {
                        // should use uib-tooltip, but angular-bootstrap-ui doesn't work (little arrow is missing)
                        s.Add($"            <div class=\"col-sm-6 col-md-3 col-lg-2\">");
                        s.Add($"                <div class=\"form-group\" data-toggle=\"tooltip\" data-placement=\"top\" title=\"From {field.Label}\">");
                        s.Add($"                    <input type=\"{(field.FieldType == FieldType.Date ? "date" : "datetime-local")}\" id=\"from{field.Name}\" name=\"from{field.Name}\" [(ngModel)]=\"searchOptions.from{field.Name}\" {(field.FieldType == FieldType.Date ? "ng-model-options=\"{timezone: 'utc'} \" " : "")}class=\"form-control\" />");
                        s.Add($"                </div>");
                        s.Add($"            </div>");
                        s.Add($"");
                        s.Add($"            <div class=\"col-sm-6 col-md-3 col-lg-2\">");
                        s.Add($"                <div class=\"form-group\" data-toggle=\"tooltip\" data-placement=\"top\" title=\"To {field.Label}\">");
                        s.Add($"                    <input type=\"{(field.FieldType == FieldType.Date ? "date" : "datetime-local")}\" id=\"to{field.Name}\" name=\"to{field.Name}\" [(ngModel)]=\"searchOptions.to{field.Name}\" {(field.FieldType == FieldType.Date ? "ng-model-options=\"{timezone: 'utc'} \" " : "")}class=\"form-control\" />");
                        s.Add($"                </div>");
                        s.Add($"            </div>");
                        s.Add($"");
                    }
                }
                s.Add($"        </div>");
                s.Add($"");
                s.Add($"        <fieldset>");
                s.Add($"");
                s.Add($"            <button type=\"submit\" class=\"btn btn-success\">Search<i class=\"fas fa-search{(CurrentEntity.Project.Bootstrap3 ? string.Empty : " ml-1")}\"></i></button>");
                if (CurrentEntity.RelationshipsAsChild.Count(r => r.Hierarchy) == 0)
                {
                    // todo: needs field list + field.newParameter
                    s.Add($"            <a [routerLink]=\"['./', 'add']\" class=\"btn btn-primary ml-1\">Add<i class=\"fas fa-plus-circle ml-1\"></i></a>");
                }
                s.Add($"");
                s.Add($"        </fieldset>");
                s.Add($"");
                s.Add($"    </form>");
            }
            s.Add($"");
            s.Add($"    <hr />");
            s.Add($"");
            // removed (not needed?): id=\"resultsList\" 
            var useSortColumn = CurrentEntity.HasASortField && !CurrentEntity.RelationshipsAsChild.Any(r => r.Hierarchy);

            s.Add($"    <table class=\"table table-bordered table-striped table-hover table-sm row-navigation\">");
            s.Add($"        <thead>");
            s.Add($"            <tr>");
            if (useSortColumn)
                s.Add($"                <th *ngIf=\"{CurrentEntity.PluralName.ToCamelCase()}.length > 1\" class=\"text-center fa-col-width\"><i class=\"fas fa-sort mt-1\"></i></th>");
            foreach (var field in CurrentEntity.Fields.Where(f => f.ShowInSearchResults).OrderBy(f => f.FieldOrder))
                s.Add($"                <th>{field.Label}</th>");
            s.Add($"            </tr>");
            s.Add($"        </thead>");
            s.Add($"        <tbody{(CurrentEntity.HasASortField && !CurrentEntity.RelationshipsAsChild.Any(r => r.Hierarchy) ? " ui-sortable=\"sortOptions\" [(ngModel)]=\"" + CurrentEntity.PluralName.ToCamelCase() + "\"" : "")}>");
            s.Add($"            <tr *ngFor=\"let {CurrentEntity.CamelCaseName} of {CurrentEntity.PluralName.ToCamelCase()}\" (click)=\"goTo{CurrentEntity.Name}({CurrentEntity.CamelCaseName})\">");
            if (useSortColumn)
                s.Add($"                <td *ngIf=\"{CurrentEntity.PluralName.ToCamelCase()}.length > 1\" class=\"text-center fa-col-width\"><i class=\"fas fa-sort sortable-handle mt-1\" ng-click=\"$event.stopPropagation();\"></i></td>");
            foreach (var field in CurrentEntity.Fields.Where(f => f.ShowInSearchResults).OrderBy(f => f.FieldOrder))
            {
                s.Add($"                <td>{field.ListFieldHtml}</td>");
            }
            s.Add($"            </tr>");
            s.Add($"        </tbody>");
            s.Add($"    </table>");
            s.Add($"");
            // entities with sort fields need to show all (pageSize = 0) for sortability, so no paging needed
            if (!(CurrentEntity.HasASortField && !CurrentEntity.RelationshipsAsChild.Any(r => r.Hierarchy)))
            {
                s.Add($"    <pager [headers]=\"headers\" (pageChanged)=\"runSearch($event)\"></pager>");
                s.Add($"");
            }
            s.Add($"</div>");
            s.Add($"");
            s.Add($"<router-outlet></router-outlet>");

            return RunCodeReplacements(s.ToString(), CodeType.ListHtml);
        }

        public string GenerateListTypeScript()
        {
            bool includeEntities = false;
            foreach (var field in CurrentEntity.Fields.Where(f => f.ShowInSearchResults).OrderBy(f => f.FieldOrder))
                if (CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(f => f.ChildFieldId == field.FieldId)))
                {
                    includeEntities = true;
                    break;
                }

            var s = new StringBuilder();

            s.Add($"import {{ Component, OnInit }} from '@angular/core';");
            s.Add($"import {{ ActivatedRoute, Router }} from '@angular/router';");
            s.Add($"import {{ Observable }} from 'rxjs';");
            s.Add($"import {{ PagingOptions }} from '../common/models/http.model';");
            s.Add($"import {{ ErrorService }} from '../common/services/error.service';");
            s.Add($"import {{ {CurrentEntity.Name}SearchOptions, {CurrentEntity.Name}SearchResponse, {CurrentEntity.Name} }} from '../common/models/{CurrentEntity.Name.ToLower()}.model';");
            s.Add($"import {{ {CurrentEntity.Name}Service }} from '../common/services/{CurrentEntity.Name.ToLower()}.service';");
            s.Add($"");
            s.Add($"@Component({{");
            s.Add($"   selector: '{CurrentEntity.Name.ToLower()}-list',");
            s.Add($"   templateUrl: './{CurrentEntity.Name.ToLower()}.list.component.html'");
            s.Add($"}})");
            s.Add($"export class {CurrentEntity.Name}ListComponent implements OnInit {{");
            s.Add($"");
            s.Add($"   private {CurrentEntity.PluralName.ToCamelCase()}: {CurrentEntity.Name}[];");
            s.Add($"   public searchOptions = new {CurrentEntity.Name}SearchOptions();");
            s.Add($"   public headers = new PagingOptions();");
            s.Add($"");
            s.Add($"   constructor(");
            s.Add($"      public route: ActivatedRoute,");
            s.Add($"      private router: Router,");
            s.Add($"      private errorService: ErrorService,");
            s.Add($"      private {CurrentEntity.Name.ToCamelCase()}Service: {CurrentEntity.Name}Service");
            s.Add($"   ) {{");
            s.Add($"   }}");
            s.Add($"");
            s.Add($"   ngOnInit(): void {{");
            // todo: this should only be when required, e.g. hierarchies?
            var hasParentHierarchy = CurrentEntity.RelationshipsAsChild.Any(r => r.Hierarchy);
            if (hasParentHierarchy)
                s.Add($"      this.searchOptions.includeEntities = true;");
            s.Add($"      this.runSearch();");
            s.Add($"   }}");
            s.Add($"");
            s.Add($"   runSearch(pageIndex: number = 0): Observable<{CurrentEntity.Name}SearchResponse> {{");
            s.Add($"");
            s.Add($"      this.searchOptions.pageIndex = pageIndex;");
            s.Add($"");
            s.Add($"      var observable = this.{CurrentEntity.Name.ToCamelCase()}Service");
            s.Add($"         .search(this.searchOptions);");
            s.Add($"");
            s.Add($"      observable.subscribe(");
            s.Add($"         response => {{");
            s.Add($"            this.{CurrentEntity.PluralName.ToCamelCase()} = response.{CurrentEntity.PluralName.ToCamelCase()};");
            s.Add($"            this.headers = response.headers;");
            s.Add($"         }},");
            s.Add($"         err => {{");
            s.Add($"");
            s.Add($"            this.errorService.handleError(err, \"{CurrentEntity.PluralName}\", \"Load\");");
            s.Add($"");
            s.Add($"         }}");
            s.Add($"      );");
            s.Add($"");
            s.Add($"      return observable;");
            s.Add($"");
            s.Add($"   }}");
            s.Add($"");
            s.Add($"   goTo{CurrentEntity.Name}({CurrentEntity.Name.ToCamelCase()}: {CurrentEntity.Name}): void {{");
            s.Add($"      this.router.navigate([{GetRouterLink(CurrentEntity)}]);");
            s.Add($"   }}");
            s.Add($"}}");
            s.Add($"");

            return RunCodeReplacements(s.ToString(), CodeType.ListTypeScript);
        }

        private string GetRouterLink(Entity entity)
        {
            string routerLink = string.Empty;

            if (entity.RelationshipsAsChild.Any(r => r.Hierarchy))
            {
                var prefix = string.Empty;
                while (entity != null)
                {
                    var nextEntity = entity.RelationshipsAsChild.SingleOrDefault(r => r.Hierarchy)?.ParentEntity;

                    routerLink = $"'{(nextEntity == null ? "/" : "")}{entity.PluralName.ToLower()}', {entity.KeyFields.Select(o => $"{prefix + entity.Name.ToCamelCase()}.{o.Name.ToCamelCase()}").Aggregate((current, next) => { return current + ", " + next; })}" + (routerLink == "" ? "" : ", ") + routerLink;

                    prefix += entity.Name.ToCamelCase() + ".";

                    entity = nextEntity;
                }
            }
            else
            {
                routerLink = $"'/{entity.PluralName.ToLower()}', { entity.KeyFields.Select(o => entity.Name.ToCamelCase() + "." + o.Name.ToCamelCase()).Aggregate((current, next) => current + ", " + next) }";
            }

            return routerLink;
        }

        public string GenerateEditHtml()
        {
            //if (CurrentEntity.EntityType == EntityType.User) return string.Empty;

            var s = new StringBuilder();

            // todo: this needs to check for hierarchies...
            // todo: what this needed:  #form=\"ngForm\"
            s.Add($"<div *ngIf=\"route.children.length === 0\">");
            s.Add($"");
            s.Add($"    <form name=\"form\" (submit)=\"save(form)\" novalidate #form=\"ngForm\" [ngClass]=\"{{ 'was-validated': form.submitted }}\">");
            s.Add($"");
            s.Add($"        <fieldset class=\"group\">");
            s.Add($"");
            s.Add($"            <legend>{CurrentEntity.FriendlyName}</legend>");
            s.Add($"");
            s.Add($"            <div class=\"form-row\">");
            s.Add($"");
            var t = string.Empty;
            // not really a bootstrap3 issue - old projects will be affected by this now being commented
            //if (CurrentEntity.Project.Bootstrap3)
            //{
            //    s.Add($"                <div class=\"col-sm-12\">");
            //    s.Add($"");
            //    t = "    ";
            //}
            #region form fields
            if (CurrentEntity.EntityType == EntityType.User)
            {
                s.Add(t + $"                <div class=\"col-sm-6 col-md-4\">");
                s.Add(t + $"                    <div class=\"form-group\" [ngClass]=\"{{ 'is-invalid': email.invalid }}\">");
                s.Add(t + $"");
                s.Add(t + $"                        <label for=\"email\">");
                s.Add(t + $"                            Email:");
                s.Add(t + $"                        </label>");
                s.Add(t + $"");
                s.Add(t + $"                        <input type=\"email\" id=\"email\" name=\"email\" [(ngModel)]=\"user.email\" #email=\"ngModel\" maxlength=\"256\" class=\"form-control\" required />");
                s.Add(t + $"");
                s.Add(t + $"                        <div *ngIf=\"email.errors?.required\" class=\"invalid-feedback\">");
                s.Add(t + $"                            Email is required");
                s.Add(t + $"                        </div>");
                s.Add(t + $"");
                s.Add(t + $"                        <div *ngIf=\"email.errors?.maxlength\" class=\"invalid-feedback\">");
                s.Add(t + $"                            Email must be at most 50 characters long");
                s.Add(t + $"                        </div>");
                s.Add(t + $"");
                s.Add(t + $"                    </div>");
                s.Add(t + $"                </div>");
                s.Add($"");
            }

            foreach (var field in CurrentEntity.Fields.OrderBy(o => o.FieldOrder))
            {
                if (field.KeyField && field.CustomType != CustomType.String && !CurrentEntity.HasCompositePrimaryKey) continue;
                if (field.EditPageType == EditPageType.Exclude) continue;
                if (field.EditPageType == EditPageType.SortField) continue;
                if (field.EditPageType == EditPageType.CalculatedField) continue;

                if (CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(f => f.ChildFieldId == field.FieldId)))
                {
                    var relationship = CurrentEntity.GetParentSearchRelationship(field);
                    //var relationshipField = relationship.RelationshipFields.Single(f => f.ChildFieldId == field.FieldId);
                    if (relationship.Hierarchy) continue;
                }

                var fieldName = field.Name.ToCamelCase();

                // todo: allow an override in the user fields?
                var controlSize = "col-sm-6 col-md-4";
                var tagType = "input";
                var attributes = new Dictionary<string, string>();

                attributes.Add("type", "text");
                attributes.Add("id", fieldName);
                attributes.Add("name", fieldName);
                attributes.Add("[(ngModel)]", CurrentEntity.Name.ToCamelCase() + "." + fieldName);
                attributes.Add("#" + fieldName, "ngModel");
                attributes.Add("class", "form-control");
                if (!field.IsNullable)
                    attributes.Add("required", null);
                if (field.FieldId == CurrentEntity.PrimaryFieldId)
                    attributes.Add("(ngModelChange)", $"changeBreadcrumb({fieldName}.value)");

                if (field.CustomType == CustomType.Boolean)
                {
                    attributes["type"] = "checkbox";
                    attributes.Remove("required");
                    attributes["class"] = "form-check-input";
                }
                else if (field.CustomType == CustomType.Number)
                {
                    attributes["type"] = "number";
                }

                if (field.Length > 0) attributes.Add("maxlength", field.Length.ToString());
                if (field.MinLength > 0) attributes.Add("minlength", field.MinLength.ToString());
                //(field.RegexValidation != null ? " ng-pattern=\"/" + field.RegexValidation + "/\"" : "") + " 

                if (CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(f => f.ChildFieldId == field.FieldId)))
                {
                    var relationship = CurrentEntity.GetParentSearchRelationship(field);
                    var relationshipField = relationship.RelationshipFields.Single(f => f.ChildFieldId == field.FieldId);
                    if (!relationship.Hierarchy && relationship.UseSelectorDirective)
                    {
                        tagType = relationship.ParentEntity.Name.Hyphenated() + "-select";
                        if (attributes.ContainsKey("type")) attributes.Remove("type");
                        if (attributes.ContainsKey("class")) attributes.Remove("class");
                        attributes.Add($"[{relationship.ParentEntity.Name.ToCamelCase()}]", $"{relationship.ChildEntity.Name.ToCamelCase()}.{relationship.ParentEntity.Name.ToCamelCase()}");
                    }
                }


                s.Add(t + $"                <div class=\"{controlSize}\">");
                s.Add(t + $"                    <div class=\"form-group\" [ngClass]=\"{{ 'is-invalid': {fieldName}.invalid }}\">");
                s.Add(t + $"");
                s.Add(t + $"                        <label for=\"{fieldName.ToCamelCase()}\">");
                s.Add(t + $"                            {field.Label}:");
                s.Add(t + $"                        </label>");
                s.Add(t + $"");

                var controlHtml = $"<{tagType}";
                foreach (var attribute in attributes)
                {
                    controlHtml += " " + attribute.Key;
                    if (attribute.Value != null) controlHtml += $"=\"{attribute.Value}\"";
                }
                if (tagType == "input")
                    controlHtml += " />";
                else
                    controlHtml += $"></{tagType}>";

                if (attributes.ContainsKey("type") && attributes["type"] == "checkbox")
                {
                    s.Add(t + $"                  <div class=\"form-check\">");
                    s.Add(t + $"                     {controlHtml}");
                    s.Add(t + $"                     <label class=\"form-check-label\" for=\"{field.Name}\">");
                    s.Add(t + $"                        {field.Label}");
                    s.Add(t + $"                     </label>");
                    s.Add(t + $"                  </div>");
                }
                else
                    s.Add(t + $"                        {controlHtml}");


                s.Add(t + $"");

                var validationErrors = new Dictionary<string, string>();
                if (!field.IsNullable && field.CustomType != CustomType.Boolean) validationErrors.Add("required", $"{field.Label} is required");
                if (field.MinLength > 0) validationErrors.Add("minlength", $"{field.Label} must be at least {field.MinLength} characters long");
                if (field.Length > 0) validationErrors.Add("maxlength", $"{field.Label} must be at most {field.Length} characters long");

                foreach (var validationError in validationErrors)
                {
                    s.Add(t + $"                        <div *ngIf=\"{fieldName}.errors?.{validationError.Key}\" class=\"invalid-feedback\">");
                    s.Add(t + $"                           {validationError.Value}");
                    s.Add(t + $"                        </div>");
                    s.Add(t + $"");
                }

                s.Add(t + $"                    </div>");
                s.Add(t + $"                </div>");
                s.Add($"");


                //if (field.EditPageType == EditPageType.ReadOnly)
                //{
                //    //s.Add(t + $"                <div class=\"col-sm-6 col-md-4\">");
                //    //s.Add(t + $"                    <div class=\"form-group\">");
                //    //s.Add(t + $"                        <label for=\"{field.Name.ToCamelCase()}\">");
                //    //s.Add(t + $"                            {field.Label}:");
                //    //s.Add(t + $"                        </label>");
                //    //if (field.CustomType == CustomType.Date)
                //    //{
                //    //    s.Add(t + $"                        <input type=\"text\" id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" class=\"form-control\" ng-disabled=\"true\" value=\"{{{{{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()} ? vm.moment({CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()}).format('DD MMMM YYYY{(field.FieldType == FieldType.Date ? string.Empty : " HH:mm" + (field.FieldType == FieldType.SmallDateTime ? "" : ":ss"))}') : ''}}}}\" />");
                //    //}
                //    //else if (field.CustomType == CustomType.Boolean)
                //    //{
                //    //    s.Add(t + $"                        <input type=\"text\" id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" class=\"form-control\" ng-disabled=\"true\" value=\"{{{{{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()} ? 'Yes' : 'No'}}}}\" />");
                //    //}
                //    //else if (CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(f => f.ChildFieldId == field.FieldId)))
                //    //{
                //    //    var relationship = CurrentEntity.GetParentSearchRelationship(field);
                //    //    s.Add(t + $"                        <input type=\"text\" id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" class=\"form-control\" ng-disabled=\"true\" value=\"{{{{{CurrentEntity.ViewModelObject}.{relationship.ParentName.ToCamelCase()}.{relationship.ParentField.Name.ToCamelCase()}}}}}\" />");
                //    //}
                //    //else
                //    //{
                //    //    s.Add(t + $"                        <input type=\"text\" id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" class=\"form-control\" ng-disabled=\"true\" value=\"{{{{{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()}}}}}\" />");
                //    //}
                //    //s.Add(t + $"                    </div>");
                //    //s.Add(t + $"                </div>");
                //    //s.Add($"");
                //}
                //else if (CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(f => f.ChildFieldId == field.FieldId)))
                //{
                //    var relationship = CurrentEntity.GetParentSearchRelationship(field);
                //    var relationshipField = relationship.RelationshipFields.Single(f => f.ChildFieldId == field.FieldId);
                //    if (relationship.Hierarchy) continue;

                //    if (relationship.UseSelectorDirective)
                //    {
                //        s.Add(t + $"                <div class=\"col-sm-6 col-md-4\">");
                //        s.Add(t + $"                    <div class=\"form-group\" ng-class=\"{{ 'has-error':  form.$submitted && form.{field.Name.ToCamelCase()}.$invalid}}\">");
                //        s.Add(t + $"                        <label>");
                //        s.Add(t + $"                            {field.Label}:");
                //        s.Add(t + $"                        </label>");
                //        s.Add(t + $"                        <{CurrentEntity.Project.AngularDirectivePrefix}-select-{relationship.ParentEntity.Name.Hyphenated().Replace(" ", "-")} id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" [(ngModel)]=\"{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()}\"{(field.IsNullable ? string.Empty : " ng-required=\"true\"")} placeholder=\"Select {field.Label.ToLower()}\" singular=\"{relationship.ParentFriendlyName}\" plural=\"{relationship.ParentEntity.PluralFriendlyName}\" {relationship.ParentEntity.Name.Hyphenated()}=\"{CurrentEntity.ViewModelObject}.{relationship.ParentName.ToCamelCase()}\"></{CurrentEntity.Project.AngularDirectivePrefix}-select-{relationship.ParentEntity.Name.Hyphenated().Replace(" ", "-")}>");
                //        s.Add(t + $"                    </div>");
                //        s.Add(t + $"                </div>");
                //        s.Add(t + $"");
                //    }
                //    else
                //    {
                //        s.Add(t + $"                <div class=\"col-sm-6 col-md-4\">");
                //        s.Add(t + $"                    <div class=\"form-group\" ng-class=\"{{ 'has-error':  form.$submitted && form.{field.Name.ToCamelCase()}.$invalid}}\">");
                //        s.Add(t + $"                        <label>");
                //        s.Add(t + $"                            {field.Label}:");
                //        s.Add(t + $"                        </label>");
                //        s.Add(t + $"                        <ol id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" class=\"nya-bs-select form-control\" [(ngModel)]=\"{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()}\"{(field.IsNullable ? string.Empty : " ng-required=\"true\"")} data-live-search=\"true\" data-size=\"10\"{(field.KeyField ? " disabled=\"!vm.isNew\"" : string.Empty)}>");
                //        s.Add(t + $"                            <li nya-bs-option=\"{relationship.ParentEntity.Name.ToCamelCase()} in vm.{relationship.ParentEntity.PluralName.ToCamelCase()}\" class=\"nya-bs-option{(CurrentEntity.Project.Bootstrap3 ? "" : " dropdown-item")}\" data-value=\"{relationship.ParentEntity.Name.ToCamelCase()}.{relationshipField.ParentField.Name.ToCamelCase()}\">");
                //        s.Add(t + $"                                <a>{{{{{relationship.ParentEntity.Name.ToCamelCase()}.{relationship.ParentField.Name.ToCamelCase()}}}}}<span class=\"fas fa-check check-mark\"></span></a>");
                //        s.Add(t + $"                            </li>");
                //        s.Add(t + $"                        </ol>");
                //        s.Add(t + $"                    </div>");
                //        s.Add(t + $"                </div>");
                //        s.Add($"");
                //    }
                //}
                //else if (field.CustomType == CustomType.Enum)
                //{
                //    s.Add(t + $"                <div class=\"col-sm-6 col-md-4\">");
                //    s.Add(t + $"                    <div class=\"form-group\" ng-class=\"{{ 'has-error':  form.$submitted && form.{field.Name.ToCamelCase()}.$invalid }}\">");
                //    s.Add(t + $"                        <label for=\"{field.Name.ToCamelCase()}\">");
                //    s.Add(t + $"                            {field.Label}:");
                //    s.Add(t + $"                        </label>");
                //    s.Add(t + $"                        <ol id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" class=\"nya-bs-select form-control\" [(ngModel)]=\"{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()}\"{(field.IsNullable ? string.Empty : " ng-required=\"true\"")} data-live-search=\"true\" data-size=\"10\">");
                //    // todo: replace with lookups
                //    s.Add(t + $"                            <li nya-bs-option=\"{field.Name.ToCamelCase()} in vm.appSettings.{field.Lookup.Name.ToCamelCase()}\" class=\"nya-bs-option{(CurrentEntity.Project.Bootstrap3 ? "" : " dropdown-item")}\" data-value=\"{field.Name.ToCamelCase()}.id\">");
                //    s.Add(t + $"                                <a>{{{{{field.Name.ToCamelCase()}.label}}}}<span class=\"fas fa-check check-mark\"></span></a>");
                //    s.Add(t + $"                            </li>");
                //    s.Add(t + $"                        </ol>");
                //    s.Add(t + $"                    </div>");
                //    s.Add(t + $"                </div>");
                //    s.Add($"");
                //}
                //else if (field.CustomType == CustomType.String)
                //{
                //var isTextArea = field.FieldType == FieldType.Text || field.FieldType == FieldType.nText || field.Length == 0;
                //s.Add(t + $"                <div class=\"{(isTextArea ? "col-sm-12" : "col-sm-6 col-md-4")}\">");
                //s.Add(t + $"                    <div class=\"form-group\" [ngClass]=\"{{ 'is-invalid': {field.Name.ToCamelCase()}.invalid }}\">");
                //s.Add(t + $"");
                //s.Add(t + $"                        <label for=\"{field.Name.ToCamelCase()}\">");
                //s.Add(t + $"                            {field.Label}:");
                //s.Add(t + $"                        </label>");
                //s.Add(t + $"");
                //if (isTextArea)
                //{
                //    s.Add(t + $"                        <textarea id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" [(ngModel)]=\"{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()}\" class=\"form-control\"{(field.IsNullable ? string.Empty : " ng-required=\"true\"")} rows=\"6\"" + (field.Length == 0 ? string.Empty : $" maxlength=\"{field.Length}\"") + (field.MinLength > 0 ? " ng-minlength=\"" + field.MinLength + "\"" : "") + $"></textarea>");
                //}
                //else
                //{
                //    s.Add(t + $"                        <input type=\"text\" id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" [(ngModel)]=\"{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()}\" #{field.Name.ToCamelCase()}=\"ngModel\" class=\"form-control\"{(field.IsNullable ? string.Empty : " required")} [ngClass]=\"{{ 'is-invalid': form.submitted && name.invalid }}\" " + (field.Length == 0 ? string.Empty : $" maxlength=\"{field.Length}\"") + (field.MinLength > 0 ? " ng-minlength=\"" + field.MinLength + "\"" : "") + (field.RegexValidation != null ? " ng-pattern=\"/" + field.RegexValidation + "/\"" : "") + " />");
                //}

                //if(!field.IsNullable)

                //s.Add(t + $"");
                //s.Add(t + $"                    </div>");
                //s.Add(t + $"                </div>");
                //s.Add($"");
                //}
                //{
                //    s.Add(t + $"                <div class=\"col-sm-4 col-md-3 col-lg-2\">");
                //    s.Add(t + $"                    <div class=\"form-group\" ng-class=\"{{ 'has-error':  form.$submitted && form.{field.Name.ToCamelCase()}.$invalid }}\">");
                //    s.Add(t + $"                        <label for=\"{field.Name.ToCamelCase()}\">");
                //    s.Add(t + $"                            {field.Label}:");
                //    s.Add(t + $"                        </label>");
                //else if (field.CustomType == CustomType.Number)
                //    s.Add(t + $"                        <input type=\"number\" id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" [(ngModel)]=\"{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()}\" class=\"form-control\"{(field.IsNullable ? string.Empty : " ng-required=\"true\"")} />");
                //    s.Add(t + $"                    </div>");
                //    s.Add(t + $"                </div>");
                //    s.Add($"");
                //}
                //else if (field.CustomType == CustomType.Boolean)
                //{
                //    s.Add(t + $"                <div class=\"col-sm-4 col-md-3 col-lg-2\">");
                //    s.Add(t + $"                    <div class=\"form-group\" ng-class=\"{{ 'has-error':  form.$submitted && form.{field.Name.ToCamelCase()}.$invalid }}\">");
                //    s.Add(t + $"                        <label for=\"{field.Name.ToCamelCase()}\">");
                //    s.Add(t + $"                            {field.Label}:");
                //    s.Add(t + $"                        </label>");
                //    //s.Add(t + $"                        <div class=\"checkbox\">");
                //    //s.Add(t + $"                            <label>");
                //    //s.Add(t + $"                                <input type=\"checkbox\" id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" [(ngModel)]=\"{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()}\" />");
                //    //s.Add(t + $"                                {field.Label}");
                //    //s.Add(t + $"                            </label>");
                //    //s.Add(t + $"                        </div>");
                //    s.Add(t + $"                        <div class=\"custom-control custom-checkbox\">");
                //    s.Add(t + $"                            <input type=\"checkbox\" id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" [(ngModel)]=\"{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()}\" class=\"custom-control-input\" />");
                //    s.Add(t + $"                            <label class=\"custom-control-label\" for=\"{field.Name.ToCamelCase()}\">{field.Label}</label>");
                //    s.Add(t + $"                        </div>");
                //    s.Add(t + $"                    </div>");
                //    s.Add(t + $"                </div>");
                //    s.Add($"");
                //}
                //else if (field.CustomType == CustomType.Date)
                //{
                //    s.Add(t + $"                <div class=\"col-sm-6 col-md-4\">");
                //    s.Add(t + $"                    <div class=\"form-group\" ng-class=\"{{ 'has-error':  form.$submitted && form.{field.Name.ToCamelCase()}.$invalid }}\">");
                //    s.Add(t + $"                        <label for=\"{field.Name.ToCamelCase()}\">");
                //    s.Add(t + $"                            {field.Label}:");
                //    s.Add(t + $"                        </label>");
                //    s.Add(t + $"                        <input type=\"{(field.FieldType == FieldType.Date ? "date" : "datetime-local")}\" id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" [(ngModel)]=\"{CurrentEntity.ViewModelObject}.{field.Name.ToCamelCase()}\"{(field.FieldType == FieldType.Date ? " ng-model-options=\"{timezone: 'utc'}\" " : "")}class=\"form-control\"{(field.IsNullable ? string.Empty : " ng-required=\"true\"")} />");
                //    s.Add(t + $"                    </div>");
                //    s.Add(t + $"                </div>");
                //    s.Add($"");
                //}
                //else
                //{
                //    s.Add(t + $"NOT IMPLEMENTED: CustomType " + field.CustomType.ToString());
                //    s.Add($"");
                //    //throw new NotImplementedException("GenerateEditHtml: NetType: " + field.NetType.ToString());
                //}
            }
            #endregion

            if (CurrentEntity.EntityType == EntityType.User)
            {
                s.Add(t + $"                <div class=\"col-sm-12 col-lg-6\">");
                s.Add(t + $"                    <div class=\"form-group\">");
                s.Add(t + $"                        <label>");
                s.Add(t + $"                            Roles:");
                s.Add(t + $"                        </label>");
                s.Add(t + $"                        <select id=\"roles\" name=\"roles\" [multiple]=\"true\" class=\"form-control\" [(ngModel)]=\"user.roleIds\">");
                s.Add(t + $"                            <option *ngFor=\"let role of roles\" [value]=\"role.id\">{{{{role.name}}}}</option>");
                s.Add(t + $"                        </select>");
                s.Add(t + $"                    </div>");
                s.Add(t + $"                </div>");
                s.Add(t + $"");
            }

            // not really a bootstrap3 issue - old projects will be affected by this now being commented
            //if (CurrentEntity.Project.Bootstrap3)
            //    s.Add(t + $"            </div>");
            s.Add($"            </div>");
            s.Add($"");
            s.Add($"        </fieldset>");
            s.Add($"");
            //s.Add($"        <div class=\"alert alert-danger\" *ngIf=\"form.submitted && form.invalid\">");
            //s.Add($"");
            //s.Add($"            <div>");
            //s.Add($"                    Please correct the following errors:");
            //s.Add($"            </div>");
            //s.Add($"");
            //s.Add($"            <div *ngIf=\"name.invalid\">");
            //s.Add($"");
            #region form validation
            if (CurrentEntity.EntityType == EntityType.User)
            {
                //s.Add($"                <li class=\"help-block has-error\" ng-messages=\"form.email.$error\">");
                //s.Add($"                    <span ng-message=\"required\">");
                //s.Add($"                        Email address is required.");
                //s.Add($"                    </span>");
                //s.Add($"                    <span ng-message=\"minlength\">");
                //s.Add($"                        Email address is too short.");
                //s.Add($"                    </span>");
                //s.Add($"                    <span ng-message=\"email\">");
                //s.Add($"                        Email address is not valid.");
                //s.Add($"                    </span>");
                //s.Add($"                </li>");
                //s.Add($"");
            }
            foreach (var field in CurrentEntity.Fields
                .Where(f => f.EditPageType != EditPageType.ReadOnly && f.EditPageType != EditPageType.Exclude && f.EditPageType != EditPageType.SortField && f.EditPageType != EditPageType.CalculatedField)
                .OrderBy(o => o.FieldOrder))
            {
                //if (field.KeyField && field.CustomType != CustomType.String && !CurrentEntity.HasCompositePrimaryKey) continue;

                //if (CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(f => f.ChildFieldId == field.FieldId)))
                //{
                //    if (field.IsNullable) continue;
                //    var relationship = CurrentEntity.GetParentSearchRelationship(field);
                //    if (relationship.Hierarchy) continue;

                //    s.Add($"                <li class=\"help-block has-error\" ng-messages=\"form.{field.Name.ToCamelCase()}.$error\">");
                //    s.Add($"                    <span ng-message=\"required\">");
                //    s.Add($"                        {field.Label} is required.");
                //    s.Add($"                    </span>");
                //    s.Add($"                </li>");
                //    s.Add($"");
                //}
                //else if (field.CustomType == CustomType.Enum || field.CustomType == CustomType.String || field.CustomType == CustomType.Number)
                //{
                //    if (field.IsNullable
                //        && field.CustomType != CustomType.Number
                //        && (field.MinLength ?? 0) == 0
                //        && field.RegexValidation == null) continue;

                //    s.Add($"                <li class=\"help-block has-error\" ng-messages=\"form.{field.Name.ToCamelCase()}.$error\">");
                //    if (!field.IsNullable)
                //    {
                //        s.Add($"                    <div *ngIf=\"name.errors.required\">");
                //        s.Add($"                        {field.Label} is required.");
                //        s.Add($"                    </div>");
                //    }
                //    if (field.CustomType == CustomType.Number)
                //    {
                //        s.Add($"                    <span ng-message=\"number\">");
                //        s.Add($"                        {field.Label} is not a valid number.");
                //        s.Add($"                    </span>");
                //    }
                //    if (field.MinLength > 0)
                //    {
                //        s.Add($"                    <span ng-message=\"minlength\">");
                //        s.Add($"                        {field.Label} is too short.");
                //        s.Add($"                    </span>");
                //    }
                //    if (field.Length > 0)
                //    {
                //        s.Add($"                    <div *ngIf=\"name.errors.maxlength\">");
                //        s.Add($"                        {field.Label} must be at most {field.Length} characters long.");
                //        s.Add($"                    </div>");
                //    }
                //    if (field.RegexValidation != null)
                //    {
                //        s.Add($"                    <span ng-message=\"pattern\">");
                //        s.Add($"                        {field.Label} is not valid.");
                //        s.Add($"                    </span>");
                //    }
                //    s.Add($"                </li>");
                //    s.Add($"");
                //}
                //else if (field.CustomType == CustomType.Date)
                //{
                //    s.Add($"                <li class=\"help-block has-error\" ng-messages=\"form.{field.Name.ToCamelCase()}.$error\">");
                //    s.Add($"                    <span ng-message=\"date\">");
                //    s.Add($"                        {field.Label} is not a valid date.");
                //    s.Add($"                    </span>");
                //    if (!field.IsNullable)
                //    {
                //        s.Add($"                    <span ng-message=\"required\">");
                //        s.Add($"                        {field.Label} is required.");
                //        s.Add($"                    </span>");
                //    }
                //    s.Add($"                </li>");
                //    s.Add($"");
                //}
                //if (field.IsNullable) continue;

            }
            #endregion
            //s.Add($"            </div>");
            //s.Add($"");
            //s.Add($"        </div>");
            //s.Add($"");

            s.Add($"        <fieldset>");
            s.Add($"            <button type=\"submit\" class=\"btn btn-success\">Save<i class=\"fas fa-check{(CurrentEntity.Project.Bootstrap3 ? string.Empty : " ml-1")}\"></i></button>");
            s.Add($"            <button type=\"button\" *ngIf=\"!isNew\" class=\"btn btn-outline-danger ml-1\" (click)=\"delete()\">Delete<i class=\"fas fa-times{(CurrentEntity.Project.Bootstrap3 ? string.Empty : " ml-1")}\"></i></button>");
            s.Add($"        </fieldset>");
            s.Add($"");
            s.Add($"    </form>");
            s.Add($"");

            #region child lists
            if (CurrentEntity.RelationshipsAsParent.Any(r => !r.ChildEntity.Exclude && r.DisplayListOnParent))
            {
                var relationships = CurrentEntity.RelationshipsAsParent.Where(r => !r.ChildEntity.Exclude && r.DisplayListOnParent).OrderBy(r => r.SortOrder);
                var counter = 0;

                s.Add($"    <div *ngIf=\"!isNew\">");
                s.Add($"");
                s.Add($"        <hr />");
                s.Add($"");
                s.Add($"        <ngb-tabset>");
                s.Add($"");
                foreach (var relationship in relationships)
                {
                    counter++;

                    s.Add($"            <ngb-tab>");
                    s.Add($"");
                    s.Add($"                <ng-template ngbTabTitle>{relationship.CollectionFriendlyName}</ng-template>");
                    s.Add($"");
                    s.Add($"                <ng-template ngbTabContent>");
                    s.Add($"");


                    var childEntity = relationship.ChildEntity;

                    if (relationship.UseMultiSelect)
                    {
                        s.Add($"                    <button class=\"btn btn-primary\" ng-click=\"add{relationship.CollectionName}()\">Add {relationship.CollectionFriendlyName}<i class=\"fas fa-plus-circle ml-1\"></i></button><br />");
                    }
                    else
                    {
                        //var href = "/";
                        //foreach (var entity in childEntity.GetNavigationEntities())
                        //{
                        //    href += (href == "/" ? string.Empty : "/") + entity.PluralName.ToLower();
                        //    foreach (var field in childEntity.GetNavigationFields().Where(f => f.EntityId == entity.EntityId))
                        //    {
                        //        if (entity == childEntity)
                        //            href += "/{{vm.appSettings." + field.NewVariable + "}}";
                        //        else
                        //            href += "/{{vm." + entity.Name.ToCamelCase() + "." + field.Name.ToCamelCase() + "}}";
                        //    }
                        //}
                        //s.Add($"                    <a class=\"btn btn-primary\" href=\"{href}\">Add {childEntity.FriendlyName}<i class=\"fas fa-plus-circle ml-1\"></i></a><br />");
                        s.Add($"                    <a [routerLink]=\"['./{childEntity.PluralName.ToLower()}', 'add']\" class=\"btn btn-primary my-3\">Add {childEntity.FriendlyName}<i class=\"fas fa-plus-circle ml-1\"></i></a><br />");
                    }

                    s.Add($"");

                    #region table
                    s.Add($"                    <table class=\"table table-bordered table-striped table-hover table-sm row-navigation\">");
                    s.Add($"                        <thead>");
                    s.Add($"                            <tr>");
                    if (relationship.Hierarchy && childEntity.HasASortField)
                        s.Add($"                                <th scope=\"col\" *ngIf=\"{relationship.CollectionName.ToCamelCase()}.length > 1\" class=\"text-center fa-col-width\"><i class=\"fas fa-sort mt-1\"></i></th>");
                    foreach (var column in childEntity.GetSearchResultsFields(CurrentEntity))
                    {
                        s.Add($"                                <th scope=\"col\">{column.Header}</th>");
                    }
                    s.Add($"                                <th scope=\"col\" class=\"fa-col-width text-center\"><i class=\"fas fa-times\"></i></th>");
                    s.Add($"                            </tr>");
                    s.Add($"                        </thead>");
                    s.Add($"                        <tbody" + (relationship.Hierarchy && childEntity.HasASortField ? $" ui-sortable=\"{childEntity.PluralName.ToCamelCase()}SortOptions\" [(ngModel)]=\"{childEntity.PluralName.ToCamelCase()}\"" : string.Empty) + ">");
                    // todo: click
                    s.Add($"                            <tr *ngFor=\"let {childEntity.Name.ToCamelCase()} of {relationship.CollectionName.ToCamelCase()}\" (click)=\"goTo{childEntity.Name}({childEntity.Name.ToCamelCase()})\">");
                    if (relationship.Hierarchy && childEntity.HasASortField)
                        s.Add($"                                <td *ngIf=\"{relationship.CollectionName.ToCamelCase()}.length > 1\" class=\"text-center fa-col-width\"><i class=\"fas fa-sort sortable-handle mt-1\" ng-click=\"$event.stopPropagation();\"></i></td>");
                    foreach (var column in childEntity.GetSearchResultsFields(CurrentEntity))
                    {
                        s.Add($"                                <td>{column.Value}</td>");
                    }
                    s.Add($"                                <td class=\"text-center\"><i class=\"fas fa-times clickable p-1 text-danger\" ng-click=\"remove{relationship.CollectionName}({relationship.ChildEntity.Name.ToCamelCase()}, $event)\"></i></td>");
                    s.Add($"                            </tr>");
                    s.Add($"                        </tbody>");
                    s.Add($"                    </table>");
                    s.Add($"");
                    s.Add($"                    <pager [headers]=\"{relationship.ChildEntity.PluralName.ToCamelCase()}Headers\" (pageChanged)=\"load{relationship.CollectionName}($event)\"></pager>");
                    s.Add($"");
                    #endregion

                    // entities with sort fields need to show all (pageSize = 0) for sortability, so no paging needed
                    if (!childEntity.HasASortField)
                    {
                        //s.Add($"                <div class=\"row\">");
                        //s.Add($"                    <div class=\"col-sm-7\">");
                        //s.Add($"                       <{CurrentEntity.Project.AngularDirectivePrefix}-pager headers=\"{relationship.ChildEntity.PluralName.ToCamelCase()}Headers\" callback=\"load{relationship.CollectionName}\"></{CurrentEntity.Project.AngularDirectivePrefix}-pager>");
                        //s.Add($"                    </div>");
                        //s.Add($"                    <div class=\"col-sm-5 text-right resultsInfo\">");
                        //s.Add($"                       <{CurrentEntity.Project.AngularDirectivePrefix}-pager-info headers=\"{relationship.ChildEntity.PluralName.ToCamelCase()}Headers\"></{CurrentEntity.Project.AngularDirectivePrefix}-pager-info>");
                        //s.Add($"                    </div>");
                        //s.Add($"                </div>");
                        //s.Add($"");
                    }

                    s.Add($"                </ng-template>");
                    s.Add($"");
                    s.Add($"            </ngb-tab>");
                    s.Add($"");
                }
                s.Add($"        </ngb-tabset>");
                s.Add($"");
                s.Add($"    </div>");
                s.Add($"");
            }
            #endregion

            s.Add($"</div>");
            s.Add($"");
            s.Add($"<router-outlet></router-outlet>");

            return RunCodeReplacements(s.ToString(), CodeType.EditHtml);
        }

        public string GenerateEditTypeScript()
        {
            var multiSelectRelationships = CurrentEntity.RelationshipsAsParent.Where(r => r.UseMultiSelect && !r.ChildEntity.Exclude).OrderBy(o => o.SortOrder);
            var relationshipsAsParent = CurrentEntity.RelationshipsAsParent.Where(r => !r.ChildEntity.Exclude && r.DisplayListOnParent).OrderBy(r => r.SortOrder);
            var relationshipsAsChildHierarchy = CurrentEntity.RelationshipsAsChild.FirstOrDefault(r => r.Hierarchy);

            var s = new StringBuilder();

            s.Add($"import {{ Component, OnInit }} from '@angular/core';");
            s.Add($"import {{ Router, ActivatedRoute }} from '@angular/router';");
            s.Add($"import {{ ToastrService }} from 'ngx-toastr';");
            s.Add($"import {{ NgForm }} from '@angular/forms';");
            // only needed if children?
            s.Add($"import {{ Observable }} from 'rxjs';");
            s.Add($"import {{ HttpErrorResponse }} from '@angular/common/http';");
            s.Add($"import {{ BreadcrumbService }} from 'angular-crumbs';");
            s.Add($"import {{ ErrorService }} from '../common/services/error.service';");
            // only needed if children?
            s.Add($"import {{ PagingOptions }} from '../common/models/http.model';");
            s.Add($"import {{ {CurrentEntity.Name} }} from '../common/models/{CurrentEntity.Name.ToLower()}.model';");
            s.Add($"import {{ {CurrentEntity.Name}Service }} from '../common/services/{CurrentEntity.Name.ToLower()}.service';");
            foreach (var rel in relationshipsAsParent)
            {
                s.Add($"import {{ {rel.ChildEntity.Name}, {rel.ChildEntity.Name}SearchOptions, {rel.ChildEntity.Name}SearchResponse }} from '../common/models/{rel.ChildEntity.Name.ToLower()}.model';");
                s.Add($"import {{ {rel.ChildEntity.Name}Service }} from '../common/services/{rel.ChildEntity.Name.ToLower()}.service';");
            }
            s.Add($"");

            s.Add($"@Component({{");
            s.Add($"   selector: '{CurrentEntity.Name.ToLower()}-edit',");
            s.Add($"   templateUrl: './{CurrentEntity.Name.ToLower()}.edit.component.html'");
            s.Add($"}})");

            s.Add($"export class {CurrentEntity.Name}EditComponent implements OnInit {{");
            s.Add($"");
            s.Add($"   public {CurrentEntity.Name.ToCamelCase()}: {CurrentEntity.Name} = new {CurrentEntity.Name}();");
            s.Add($"   public isNew: boolean = true;");
            if (CurrentEntity.EntityType == EntityType.User)
                // todo: fix
                s.Add($"   public roles = [{{ name: 'Administrator', id: '470356a5-f7db-4e2e-9c99-62c2800dc2f4' }}];");
            s.Add($"");
            foreach (var rel in relationshipsAsParent)
            {
                s.Add($"   public {rel.CollectionName.ToCamelCase()}SearchOptions = new {rel.ChildEntity.Name}SearchOptions();");
                s.Add($"   public {rel.CollectionName.ToCamelCase()}Headers = new PagingOptions();");
                s.Add($"   public {rel.CollectionName.ToCamelCase()}: {rel.ChildEntity.Name}[];");
                s.Add($"");
            }

            s.Add($"   constructor(");
            s.Add($"      private router: Router,");
            s.Add($"      public route: ActivatedRoute,");
            s.Add($"      private toastr: ToastrService,");
            s.Add($"      private breadcrumbService: BreadcrumbService,");
            s.Add($"      private errorService: ErrorService,");
            s.Add($"      private {CurrentEntity.Name.ToCamelCase()}Service: {CurrentEntity.Name}Service,");
            foreach (var rel in relationshipsAsParent)
            {
                s.Add($"      private {rel.ChildEntity.Name.ToCamelCase()}Service: {rel.ChildEntity.Name}Service" + (rel == relationshipsAsParent.Last() ? "" : ","));
            }
            s.Add($"   ) {{");
            s.Add($"   }}");
            s.Add($"");

            s.Add($"   ngOnInit() {{");
            s.Add($"");
            s.Add($"      this.route.params.subscribe(params => {{");
            s.Add($"");
            foreach (var keyField in CurrentEntity.KeyFields)
            {
                s.Add($"         let {keyField.Name.ToCamelCase()} = params[\"{keyField.Name.ToCamelCase()}\"];");
                // todo: what happens for multiple fields?
                s.Add($"         this.isNew = {keyField.Name.ToCamelCase()} === \"add\";");
            }
            s.Add($"");
            s.Add($"         if (!this.isNew) {{");
            s.Add($"");
            foreach (var keyField in CurrentEntity.KeyFields)
            {
                s.Add($"            this.{CurrentEntity.Name.ToCamelCase()}.{keyField.Name.ToCamelCase()} = {keyField.Name.ToCamelCase()};");
            }
            s.Add($"            this.load{CurrentEntity.Name}();");
            s.Add($"");
            foreach (var rel in relationshipsAsParent)
            {
                foreach (var relField in rel.RelationshipFields)
                    s.Add($"            this.{rel.CollectionName.ToCamelCase()}SearchOptions.{relField.ChildField.Name.ToCamelCase()} = {relField.ParentField.Name.ToCamelCase()};");
                s.Add($"            this.{rel.CollectionName.ToCamelCase()}SearchOptions.includeEntities = true; // can remove if using relative routerLink");
                s.Add($"            this.load{rel.CollectionName}();");
                s.Add($"");
            }

            s.Add($"         }}");
            if (relationshipsAsChildHierarchy != null)
            {
                s.Add($"         else {{");
                foreach (var field in relationshipsAsChildHierarchy.RelationshipFields)
                    s.Add($"            this.{CurrentEntity.Name.ToCamelCase()}.{field.ChildField.Name.ToCamelCase()} = this.route.snapshot.parent.params.{field.ParentField.Name.ToCamelCase()};");
                s.Add($"         }}");
            }
            s.Add($"");
            s.Add($"      }});");
            s.Add($"");
            s.Add($"   }}");
            s.Add($"");

            s.Add($"   private load{CurrentEntity.Name}() {{");
            s.Add($"");
            s.Add($"      this.{CurrentEntity.Name.ToCamelCase()}Service.get({CurrentEntity.KeyFields.Select(o => $"this.{CurrentEntity.Name.ToCamelCase()}.{o.Name.ToCamelCase()}").Aggregate((current, next) => { return current + ", " + next; })})");
            s.Add($"         .subscribe(");
            s.Add($"            {CurrentEntity.Name.ToCamelCase()} => {{");
            s.Add($"               this.{CurrentEntity.Name.ToCamelCase()} = {CurrentEntity.Name.ToCamelCase()};");
            s.Add($"               this.changeBreadcrumb(this.{CurrentEntity.Name.ToCamelCase()}.{CurrentEntity.PrimaryField.Name.ToCamelCase() + (CurrentEntity.PrimaryField.JavascriptType == "string" ? "" : ".toString()")});");
            s.Add($"            }},");
            s.Add($"            err => {{");
            s.Add($"               this.errorService.handleError(err, \"{CurrentEntity.Name}\", \"Load\");");
            s.Add($"               if (err instanceof HttpErrorResponse && err.status === 404)");
            s.Add($"                  this.router.navigate([\"/{CurrentEntity.PluralName.ToLower()}\"]);");
            s.Add($"            }}");
            s.Add($"         );");
            s.Add($"");
            s.Add($"   }}");
            s.Add($"");

            s.Add($"   save(form: NgForm): void {{");
            s.Add($"");
            s.Add($"      if (form.invalid) {{");
            s.Add($"");
            s.Add($"         this.toastr.error(\"The form has not been completed correctly.\", \"Form Error\");");
            s.Add($"         return;");
            s.Add($"");
            s.Add($"      }}");
            s.Add($"");
            s.Add($"      this.{CurrentEntity.Name.ToCamelCase()}Service.save(this.{CurrentEntity.Name.ToCamelCase()})");
            s.Add($"         .subscribe(");
            s.Add($"            {CurrentEntity.Name.ToCamelCase()} => {{");
            s.Add($"               this.toastr.success(\"The {CurrentEntity.FriendlyName.ToLower()} has been saved\", \"Save {CurrentEntity.FriendlyName}\");");
            s.Add($"               if (this.isNew) this.router.navigate([\"../\", {CurrentEntity.KeyFields.Select(o => $"{CurrentEntity.Name.ToCamelCase()}.{o.Name.ToCamelCase()}").Aggregate((current, next) => { return current + ", " + next; })}], {{ relativeTo: this.route }});");
            s.Add($"            }},");
            s.Add($"            err => {{");
            s.Add($"               this.errorService.handleError(err, \"{CurrentEntity.FriendlyName}\", \"Save\");");
            s.Add($"            }}");
            s.Add($"         );");
            s.Add($"");
            s.Add($"   }}");
            s.Add($"");

            s.Add($"   delete(): void {{");
            s.Add($"");
            // todo: make this a modal?
            s.Add($"      if (!confirm(\"Confirm delete?\")) return;");
            s.Add($"");
            s.Add($"      this.{CurrentEntity.Name.ToCamelCase()}Service.delete({CurrentEntity.KeyFields.Select(o => $"this.{CurrentEntity.Name.ToCamelCase()}.{o.Name.ToCamelCase()}").Aggregate((current, next) => { return current + ", " + next; })})");
            s.Add($"         .subscribe(");
            s.Add($"            () => {{");
            s.Add($"               this.toastr.success(\"The {CurrentEntity.FriendlyName.ToLower()} has been deleted\", \"Delete {CurrentEntity.FriendlyName}\");");
            s.Add($"               this.router.navigate([\"/{CurrentEntity.PluralName.ToLower()}\"]);");
            s.Add($"            }},");
            s.Add($"            err => {{");
            s.Add($"               this.errorService.handleError(err, \"{CurrentEntity.FriendlyName}\", \"Delete\");");
            s.Add($"            }}");
            s.Add($"         );");
            s.Add($"");
            s.Add($"   }}");
            s.Add($"");

            s.Add($"   changeBreadcrumb(label: string): void {{");
            s.Add($"      this.breadcrumbService.changeBreadcrumb(this.route.snapshot, label || \"(no {CurrentEntity.PrimaryField.Label.ToLower()})\");");
            s.Add($"   }}");
            s.Add($"");

            foreach (var rel in relationshipsAsParent)
            {
                if (!rel.DisplayListOnParent && !rel.Hierarchy) continue;

                s.Add($"   load{rel.CollectionName}(pageIndex: number = 0): Observable<{rel.ChildEntity.Name}SearchResponse> {{");
                s.Add($"");
                s.Add($"      this.{rel.CollectionName.ToCamelCase()}SearchOptions.pageIndex = pageIndex;");
                s.Add($"");
                s.Add($"      var observable = this.{rel.ChildEntity.Name.ToCamelCase()}Service");
                s.Add($"         .search(this.{rel.CollectionName.ToCamelCase()}SearchOptions);");
                s.Add($"");
                s.Add($"      observable.subscribe(");
                s.Add($"         response => {{");
                s.Add($"            this.{rel.CollectionName.ToCamelCase()} = response.{rel.ChildEntity.PluralName.ToCamelCase()};");
                s.Add($"            this.{rel.CollectionName.ToCamelCase()}Headers = response.headers;");
                s.Add($"         }},");
                s.Add($"         err => {{");
                s.Add($"");
                s.Add($"            this.errorService.handleError(err, \"{rel.CollectionFriendlyName}\", \"Load\");");
                s.Add($"");
                s.Add($"         }}");
                s.Add($"      );");
                s.Add($"");
                s.Add($"      return observable;");
                s.Add($"");
                s.Add($"   }}");
                s.Add($"");
                // todo: use relative links? can then disable 'includeEntities' on these entities
                s.Add($"   goTo{rel.ChildEntity.Name}({rel.ChildEntity.Name.ToCamelCase()}: {rel.ChildEntity.Name}) {{");
                s.Add($"      this.router.navigate([{GetRouterLink(rel.ChildEntity)}]);");
                s.Add($"   }}");
                s.Add($"");
            }

            s.Add($"}}");

            return RunCodeReplacements(s.ToString(), CodeType.EditTypeScript);

        }

        public string GenerateAppSelectHtml()
        {
            var s = new StringBuilder();

            var file = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "templates/appselect.html");
            s.Add(RunTemplateReplacements(file));

            return RunCodeReplacements(s.ToString(), CodeType.AppSelectHtml);
        }

        public string GenerateAppSelectTypeScript()
        {
            var s = new StringBuilder();

            var file = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "templates/appselect.ts.txt");

            var filterAttributes = string.Empty;
            var filterWatches = string.Empty;
            var filterOptions = string.Empty;

            foreach (var field in CurrentEntity.Fields.Where(o => o.SearchType == SearchType.Exact && (o.FieldType == FieldType.Enum || CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(f => f.ChildFieldId == o.FieldId)))))
            {
                var name = field.Name.ToCamelCase();

                Relationship relationship = null;
                if (CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(f => f.ChildFieldId == field.FieldId)))
                {
                    relationship = CurrentEntity.GetParentSearchRelationship(field);
                    name = relationship.ParentName.ToCamelCase();
                }

                filterAttributes += $",{Environment.NewLine}                {name}: \"<\"";

                // this will probably error on enum (non-relationship) searches, as it needs to have the appropriate fields set in the 
                // second part of the check, i.e. after the 'newValue !== oldValue && ...'
                filterWatches += Environment.NewLine;
                filterWatches += $"        $scope.$watch(\"{name}\", (newValue, oldValue) => {{" + Environment.NewLine;
                filterWatches += $"            if (newValue !== oldValue{(relationship == null ? "" : $" && $scope.{CurrentEntity.CamelCaseName} && newValue.{relationship.RelationshipFields.Single().ParentField.Name.ToCamelCase()} !== $scope.{CurrentEntity.CamelCaseName}.{field.Name.ToCamelCase()} && !$scope.removeFilters")}) {{" + Environment.NewLine;
                filterWatches += $"                $scope.ngModel = undefined;" + Environment.NewLine;
                filterWatches += $"                $scope.{CurrentEntity.CamelCaseName} = undefined;" + Environment.NewLine;
                filterWatches += $"            }}" + Environment.NewLine;
                filterWatches += $"        }});" + Environment.NewLine;

                filterOptions += $",{Environment.NewLine}                            {name}: $scope.{name}";
            }

            if (CurrentEntity.PrimaryField == null) throw new Exception("Entity " + CurrentEntity.Name + " does not have a Primary Field defined for AppSelect label");

            file = RunTemplateReplacements(file)
                .Replace("/*FILTER_ATTRIBUTES*/", filterAttributes)
                .Replace("/*FILTER_WATCHES*/", filterWatches)
                .Replace("/*FILTER_OPTIONS*/", filterOptions)
                .Replace("LABELFIELD", CurrentEntity.PrimaryField.Name.ToCamelCase());

            s.Add(file);

            return RunCodeReplacements(s.ToString(), CodeType.AppSelectTypeScript);
        }

        public string GenerateSelectModalHtml()
        {
            var s = new StringBuilder();

            var file = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "templates/selectmodal.html");

            var fieldHeaders = string.Empty;
            var fieldList = string.Empty;
            var appSelectFilters = string.Empty;
            var filterAlerts = string.Empty;

            foreach (var field in CurrentEntity.Fields.Where(f => f.ShowInSearchResults).OrderBy(f => f.FieldOrder))
            {
                var ngIf = string.Empty;
                if (CurrentEntity.Fields.Any(o => o.FieldId == field.FieldId && o.SearchType == SearchType.Exact))
                {
                    if (field.FieldType == FieldType.Enum)
                        ngIf = " *ngIf=\"!" + field.Name.ToCamelCase() + $"\"";
                    else
                    {
                        if (CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(rf => rf.ChildFieldId == field.FieldId) && r.UseSelectorDirective))
                        {
                            var relationship = CurrentEntity.GetParentSearchRelationship(field);
                            ngIf = " *ngIf=\"!" + relationship.ParentName.ToCamelCase().ToCamelCase() + $"\"";
                        }
                    }
                }

                fieldHeaders += (fieldHeaders == string.Empty ? string.Empty : Environment.NewLine) + $"                <th scope=\"col\"{ngIf}>{field.Label}</th>";
                fieldList += (fieldList == string.Empty ? string.Empty : Environment.NewLine);

                fieldList += $"                <td{ngIf}>{field.ListFieldHtml}</td>";
            }

            foreach (var field in CurrentEntity.Fields.Where(f => f.SearchType == SearchType.Exact).OrderBy(f => f.FieldOrder))
            {
                Relationship relationship = null;
                if (CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(rf => rf.ChildFieldId == field.FieldId) && r.UseSelectorDirective))
                    relationship = CurrentEntity.GetParentSearchRelationship(field);

                if (field.FieldType == FieldType.Enum || relationship != null)
                {
                    appSelectFilters += Environment.NewLine;
                    if (field.FieldType == FieldType.Enum)
                    {
                        appSelectFilters += $"                    <div class=\"col-sm-6 col-md-6 col-lg-4\" *ngIf=\"!{field.Name.ToCamelCase()}\">" + Environment.NewLine;
                        appSelectFilters += $"                        <ol id=\"{field.Name.ToCamelCase()}\" name=\"{field.Name.ToCamelCase()}\" title=\"{field.Label}\" class=\"nya-bs-select form-control\" [(ngModel)]=\"search.{field.Name.ToCamelCase()}\" data-live-search=\"true\" data-size=\"10\">" + Environment.NewLine;
                        appSelectFilters += $"                            <li nya-bs-option=\"item in vm.appSettings.{field.Lookup.Name.ToCamelCase()}\" class=\"nya-bs-option{(CurrentEntity.Project.Bootstrap3 ? "" : " dropdown-item")}\" data-value=\"item.id\">" + Environment.NewLine;
                        appSelectFilters += $"                                <a>{{{{item.label}}}}<span class=\"fas fa-check check-mark\"></span></a>" + Environment.NewLine;
                        appSelectFilters += $"                            </li>" + Environment.NewLine;
                        appSelectFilters += $"                        </ol>" + Environment.NewLine;
                        appSelectFilters += $"                    </div>" + Environment.NewLine;
                    }
                    else
                    {
                        appSelectFilters += $"                    <div class=\"col-sm-6 col-md-6 col-lg-4\" *ngIf=\"!{relationship.ParentName.ToCamelCase()}\">" + Environment.NewLine;
                        appSelectFilters += $"                        <div class=\"form-group\">" + Environment.NewLine;
                        appSelectFilters += $"                            {relationship.AppSelector}" + Environment.NewLine;
                        appSelectFilters += $"                        </div>" + Environment.NewLine;
                        appSelectFilters += $"                    </div>" + Environment.NewLine;
                    }

                    if (filterAlerts == string.Empty) filterAlerts = Environment.NewLine;

                    if (field.FieldType == FieldType.Enum)
                        filterAlerts += $"                <div class=\"alert alert-info alert-dismissible\" *ngIf=\"{field.Name.ToCamelCase()}\"><button type=\"button\" class=\"close\" data-dismiss=\"alert\" aria-label=\"Close\" ng-click=\"search.{field.Name.ToCamelCase()}=undefined;\"><span aria-hidden=\"true\" *ngIf=\"removeFilters\">&times;</span></button>Filtered by {field.Label.ToLower()}: {{{{{field.Name.ToCamelCase()}.label}}}}</div>" + Environment.NewLine;
                    else
                        filterAlerts += $"                <div class=\"alert alert-info alert-dismissible\" *ngIf=\"{relationship.ParentName.ToCamelCase()}\"><button type=\"button\" class=\"close\" data-dismiss=\"alert\" aria-label=\"Close\" ng-click=\"search.{field.Name.ToCamelCase()}=undefined;\"><span aria-hidden=\"true\" *ngIf=\"removeFilters\">&times;</span></button>Filtered by {field.Label.ToLower()}: {{{{{relationship.ParentName.ToCamelCase()}.{relationship.ParentField.Name.ToCamelCase()}}}}}</div>" + Environment.NewLine;
                }
            }

            file = RunTemplateReplacements(file)
                .Replace("FIELD_HEADERS", fieldHeaders)
                .Replace("FIELD_LIST", fieldList)
                .Replace("APP_SELECT_FILTERS", appSelectFilters)
                .Replace("FILTER_ALERTS", filterAlerts);

            s.Add(file);

            return RunCodeReplacements(s.ToString(), CodeType.SelectModalHtml);
        }

        public string GenerateSelectModalTypeScript()
        {
            var s = new StringBuilder();

            var file = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "templates/selectmodal.ts.txt");

            var filterParams = string.Empty;
            var filterTriggers = string.Empty;

            foreach (var field in CurrentEntity.Fields.Where(o => o.SearchType == SearchType.Exact))
            {
                Relationship relationship = null;
                if (CurrentEntity.RelationshipsAsChild.Any(r => r.RelationshipFields.Any(f => f.ChildFieldId == field.FieldId)))
                    relationship = CurrentEntity.GetParentSearchRelationship(field);

                if (field.FieldType == FieldType.Enum)
                    filterParams += $"{Environment.NewLine}                {field.Name.ToCamelCase()}: (options.{field.Name.ToCamelCase()} ? options.{field.Name.ToCamelCase()}.id : undefined),";
                else if (relationship != null)
                    filterParams += $"{Environment.NewLine}                {field.Name.ToCamelCase()}: (options.{relationship.ParentName.ToCamelCase()} ? options.{relationship.ParentName.ToCamelCase()}.{relationship.RelationshipFields.Single().ParentField.Name.ToCamelCase()} : undefined),";

                if (relationship != null && relationship.UseSelectorDirective)
                {
                    filterTriggers += Environment.NewLine;
                    filterTriggers += $"            $scope.$watch(\"vm.search.{relationship.RelationshipFields.Single().ChildField.Name.ToCamelCase()}\", (newValue, oldValue) => {{" + Environment.NewLine;
                    filterTriggers += $"                if (newValue !== oldValue) run{CurrentEntity.Name}Search(0, false);" + Environment.NewLine;
                    filterTriggers += $"            }});" + Environment.NewLine;
                }
            }

            file = RunTemplateReplacements(file)
                .Replace("/*FILTER_PARAMS*/", filterParams)
                .Replace("/*FILTER_TRIGGERS*/", filterTriggers);

            s.Add(file);

            return RunCodeReplacements(s.ToString(), CodeType.SelectModalTypeScript);
        }

        private string RunTemplateReplacements(string input)
        {
            return input
                .Replace("PLURALNAME_TOCAMELCASE", CurrentEntity.PluralName.ToCamelCase())
                .Replace("CAMELCASENAME", CurrentEntity.CamelCaseName)
                .Replace("PLURALFRIENDLYNAME_TOLOWER", CurrentEntity.PluralFriendlyName.ToLower())
                .Replace("PLURALFRIENDLYNAME", CurrentEntity.PluralFriendlyName)
                .Replace("FRIENDLYNAME_LOWER", CurrentEntity.FriendlyName.ToLower())
                .Replace("FRIENDLYNAME", CurrentEntity.FriendlyName)
                .Replace("PLURALNAME", CurrentEntity.PluralName)
                .Replace("NAME_TOLOWER", CurrentEntity.Name.ToLower())
                .Replace("HYPHENATEDNAME", CurrentEntity.Name.Hyphenated())
                .Replace("KEYFIELD", CurrentEntity.KeyFields.Single().Name.ToCamelCase())
                .Replace("NAME", CurrentEntity.Name)
                .Replace("ICONLINK", GetIconLink(CurrentEntity))
                .Replace("ADDNEWURL", CurrentEntity.PluralName.ToLower() + "/{{vm.appSettings.newGuid}}")
                .Replace("// <reference", "/// <reference");
        }

        private string GetIconLink(Entity entity)
        {
            if (String.IsNullOrWhiteSpace(entity.IconClass)) return string.Empty;

            string html = $@"<span class=""input-group-btn"" *ngIf=""!multiple && !!{entity.Name.ToLower()}"">
        <a href=""{GetHierarchyString(entity)}"" class=""btn btn-secondary"" ng-disabled=""disabled"">
            <i class=""fas {entity.IconClass}""></i>
        </a>
    </span>
    ";
            return html;
        }

        // ---- HELPER METHODS -----------------------------------------------------------------------

        private string GetHierarchyString(Entity entity, string prefix = null)
        {
            var hierarchyRelationship = entity.RelationshipsAsChild.SingleOrDefault(o => o.Hierarchy);
            var parents = "";
            if (hierarchyRelationship != null)
            {
                parents = GetHierarchyString(hierarchyRelationship.ParentEntity, (prefix == null ? "" : prefix + ".") + entity.Name.ToCamelCase());
            }
            return parents + "/" + entity.PluralName.ToLower() + "/{{" + (prefix == null ? "" : prefix + ".") + entity.Name.ToCamelCase() + "." + entity.KeyFields.Single().Name.ToCamelCase() + "}}";
        }

        private string ClimbHierarchy(Relationship relationship, string result)
        {
            result += "." + relationship.ParentName;
            foreach (var relAbove in relationship.ParentEntity.RelationshipsAsChild.Where(r => r.Hierarchy))
                result = ClimbHierarchy(relAbove, result);
            return result;
        }

        private string RunCodeReplacements(string code, CodeType type)
        {

            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventApiResourceDeployment) && type == CodeType.ApiResource) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventAppRouterDeployment) && type == CodeType.AppRouter) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventBundleConfigDeployment) && type == CodeType.BundleConfig) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventControllerDeployment) && type == CodeType.Controller) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventDbContextDeployment) && type == CodeType.DbContext) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventDTODeployment) && type == CodeType.DTO) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventEditHtmlDeployment) && type == CodeType.EditHtml) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventEditTypeScriptDeployment) && type == CodeType.EditTypeScript) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventListHtmlDeployment) && type == CodeType.ListHtml) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventListTypeScriptDeployment) && type == CodeType.ListTypeScript) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventModelDeployment) && type == CodeType.Model) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventAppSelectHtmlDeployment) && type == CodeType.AppSelectHtml) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventAppSelectTypeScriptDeployment) && type == CodeType.AppSelectTypeScript) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventSelectModalHtmlDeployment) && type == CodeType.SelectModalHtml) return code;
            if (!String.IsNullOrWhiteSpace(CurrentEntity.PreventSelectModalTypeScriptDeployment) && type == CodeType.SelectModalTypeScript) return code;

            // todo: needs a sort order

            var replacements = CurrentEntity.CodeReplacements.Where(cr => !cr.Disabled && cr.CodeType == type).ToList();
            replacements.InsertRange(0, DbContext.CodeReplacements.Where(o => o.Entity.ProjectId == CurrentEntity.ProjectId && !o.Disabled && o.CodeType == CodeType.Global).ToList());

            // common scripts need a common replacement
            if (type == CodeType.Enums || type == CodeType.ApiResource || type == CodeType.AppRouter || type == CodeType.BundleConfig || type == CodeType.DbContext)
                replacements = CodeReplacements.Where(cr => !cr.Disabled && cr.CodeType == type && cr.Entity.ProjectId == CurrentEntity.ProjectId).ToList();

            foreach (var replacement in replacements.OrderBy(o => o.SortOrder))
            {
                var findCode = replacement.FindCode.Replace("(", "\\(").Replace(")", "\\)").Replace("[", "\\[").Replace("]", "\\]").Replace("?", "\\?").Replace("*", "\\*").Replace("$", "\\$").Replace("+", "\\+").Replace("{", "\\{").Replace("}", "\\}").Replace("|", "\\|").Replace("\n", "\r\n").Replace("\r\r", "\r");
                var re = new Regex(findCode);
                if (replacement.CodeType != CodeType.Global && !re.IsMatch(code))
                    throw new Exception($"{CurrentEntity.Name} failed to replace {replacement.Purpose} in {replacement.Entity.Name}.{type.ToString()}");
                code = re.Replace(code, replacement.ReplacementCode ?? string.Empty).Replace("\n", "\r\n").Replace("\r\r", "\r");
            }
            return code;
        }

        private string GetKeyFieldLinq(string entityName, string otherEntityName = null, string comparison = "==", string joiner = "&&", bool addParenthesisIfMultiple = false)
        {
            return (addParenthesisIfMultiple && CurrentEntity.KeyFields.Count() > 1 ? "(" : "") +
                CurrentEntity.KeyFields
                    .Select(o => $"{entityName}.{o.Name} {comparison} " + (otherEntityName == null ? o.Name.ToCamelCase() : $"{otherEntityName}.{o.Name}")).Aggregate((current, next) => $"{current} {joiner} {next}")
                    + (addParenthesisIfMultiple && CurrentEntity.KeyFields.Count() > 1 ? ")" : "")
                    ;
        }

        public List<string> GetTopAncestors(List<string> list, string prefix, Relationship relationship, RelationshipAncestorLimits ancestorLimit, int level = 0, bool includeIfHierarchy = false)
        {
            // override is for the controller.get, when in a hierarchy. need to return the full hierarchy, so that the breadcrumb will be set correctly
            // change: commented out ancestorLimit as (eg) KTUPACK Recommendation-Topic had IncludeAllParents but was therefore setting overrideLimit to false
            var overrideLimit = relationship.Hierarchy && includeIfHierarchy && ancestorLimit != RelationshipAncestorLimits.IncludeAllParents;

            //if (relationship.RelationshipAncestorLimit == RelationshipAncestorLimits.Exclude) return list;
            prefix += "." + relationship.ParentName;
            if (!overrideLimit && ancestorLimit == RelationshipAncestorLimits.IncludeRelatedEntity && level == 0)
            {
                list.Add(prefix);
            }
            else if (!overrideLimit && ancestorLimit == RelationshipAncestorLimits.IncludeRelatedParents && level == 1)
            {
                list.Add(prefix);
            }
            else if (includeIfHierarchy && relationship.Hierarchy)
            {
                var hierarchyRel = relationship.ParentEntity.RelationshipsAsChild.SingleOrDefault(o => o.Hierarchy);
                if (hierarchyRel != null)
                {
                    list = GetTopAncestors(list, prefix, hierarchyRel, ancestorLimit, level + 1, includeIfHierarchy);
                }
                else if (includeIfHierarchy)
                {
                    list.Add(prefix);
                }
            }
            else if (relationship.ParentEntity.RelationshipsAsChild.Any() && relationship.ParentEntityId != relationship.ChildEntityId)
            {
                foreach (var parentRelationship in relationship.ParentEntity.RelationshipsAsChild.Where(r => r.RelationshipAncestorLimit != RelationshipAncestorLimits.Exclude))
                {
                    // if building the hierarchy links, only continue adding if it's still in the hierarchy
                    if (includeIfHierarchy && !parentRelationship.Hierarchy) continue;

                    // if got here because not overrideLimit, otherwise if it IS, then only if the parent relationship is the hierarchy
                    if (overrideLimit || parentRelationship.Hierarchy)
                        list = GetTopAncestors(list, prefix, parentRelationship, ancestorLimit, level + 1, includeIfHierarchy);
                }
                //if (list.Count == 0 && includeIfHierarchy) list.Add();
            }
            else
            {
                list.Add(prefix);
            }
            return list;
        }

        public string Validate()
        {
            if (CurrentEntity.Fields.Count == 0) return "No fields are defined";
            if (CurrentEntity.KeyFields.Count == 0) return "No key fields are defined";
            if (!CurrentEntity.Fields.Any(f => f.ShowInSearchResults)) return "No fields are designated as search result fields";
            var rel = CurrentEntity.RelationshipsAsChild.FirstOrDefault(r => r.RelationshipFields.Count == 0);
            if (rel != null) return $"Relationship {rel.CollectionName} (to {rel.ParentEntity.FriendlyName}) has no link fields defined";
            rel = CurrentEntity.RelationshipsAsParent.FirstOrDefault(r => r.RelationshipFields.Count == 0);
            if (rel != null) return $"Relationship {rel.CollectionName} (to {rel.ChildEntity.FriendlyName}) has no link fields defined";
            //if (CurrentEntity.RelationshipsAsChild.Where(r => r.Hierarchy).Count() > 1) return $"{CurrentEntity.Name} is a hierarchical child on more than one relationship";
            if (CurrentEntity.RelationshipsAsParent.Any(r => r.UseMultiSelect && !r.DisplayListOnParent)) return "Using Multi-Select requires that the relationship is also displayed on the parent";
            return null;
        }

        public static string RunDeployment(ApplicationDbContext DbContext, Entity entity, DeploymentOptions deploymentOptions)
        {
            var codeGenerator = new Code(entity, DbContext);

            var error = codeGenerator.Validate();
            if (error != null)
                return (error);

            if (!Directory.Exists(entity.Project.RootPath))
                return ("Project path does not exist");

            if (deploymentOptions.Model && !string.IsNullOrWhiteSpace(entity.PreventModelDeployment))
                return ("Model deployment is not allowed: " + entity.PreventModelDeployment);
            if (deploymentOptions.DTO && !string.IsNullOrWhiteSpace(entity.PreventDTODeployment))
                return ("DTO deployment is not allowed: " + entity.PreventDTODeployment);
            if (deploymentOptions.DbContext && !string.IsNullOrWhiteSpace(entity.PreventDbContextDeployment))
                return ("DbContext deployment is not allowed: " + entity.PreventDbContextDeployment);
            if (deploymentOptions.Controller && !string.IsNullOrWhiteSpace(entity.PreventControllerDeployment))
                return ("Controller deployment is not allowed: " + entity.PreventControllerDeployment);
            if (deploymentOptions.BundleConfig && !string.IsNullOrWhiteSpace(entity.PreventBundleConfigDeployment))
                return ("BundleConfig deployment is not allowed: " + entity.PreventBundleConfigDeployment);
            if (deploymentOptions.AppRouter && !string.IsNullOrWhiteSpace(entity.PreventAppRouterDeployment))
                return ("AppRouter deployment is not allowed: " + entity.PreventAppRouterDeployment);
            if (deploymentOptions.ApiResource && !string.IsNullOrWhiteSpace(entity.PreventApiResourceDeployment))
                return ("ApiResource deployment is not allowed: " + entity.PreventApiResourceDeployment);
            if (deploymentOptions.ListHtml && !string.IsNullOrWhiteSpace(entity.PreventListHtmlDeployment))
                return ("ListHtml deployment is not allowed: " + entity.PreventListHtmlDeployment);
            if (deploymentOptions.ListTypeScript && !string.IsNullOrWhiteSpace(entity.PreventListTypeScriptDeployment))
                return ("ListTypeScript deployment is not allowed: " + entity.PreventListTypeScriptDeployment);
            if (deploymentOptions.EditHtml && !string.IsNullOrWhiteSpace(entity.PreventEditHtmlDeployment))
                return ("EditHtml deployment is not allowed: " + entity.PreventEditHtmlDeployment);
            if (deploymentOptions.EditTypeScript && !string.IsNullOrWhiteSpace(entity.PreventEditTypeScriptDeployment))
                return ("EditTypeScript deployment is not allowed: " + entity.PreventEditTypeScriptDeployment);
            if (deploymentOptions.AppSelectHtml && !string.IsNullOrWhiteSpace(entity.PreventAppSelectHtmlDeployment))
                return ("AppSelectHtml deployment is not allowed: " + entity.PreventAppSelectHtmlDeployment);
            if (deploymentOptions.AppSelectTypeScript && !string.IsNullOrWhiteSpace(entity.PreventAppSelectTypeScriptDeployment))
                return ("AppSelectTypeScript deployment is not allowed: " + entity.PreventAppSelectTypeScriptDeployment);
            if (deploymentOptions.SelectModalHtml && !string.IsNullOrWhiteSpace(entity.PreventSelectModalHtmlDeployment))
                return ("SelectModalHtml deployment is not allowed: " + entity.PreventSelectModalHtmlDeployment);
            if (deploymentOptions.SelectModalTypeScript && !string.IsNullOrWhiteSpace(entity.PreventSelectModalTypeScriptDeployment))
                return ("SelectModalTypeScript deployment is not allowed: " + entity.PreventSelectModalTypeScriptDeployment);

            if (deploymentOptions.DbContext)
            {
                var firstEntity = DbContext.Entities.SingleOrDefault(e => e.ProjectId == entity.ProjectId && e.PreventDbContextDeployment.Length > 0);
                if (firstEntity != null)
                    return ("DbContext deployment is not allowed on " + firstEntity.Name + ": " + entity.PreventDbContextDeployment);
            }
            if (deploymentOptions.BundleConfig)
            {
                var firstEntity = DbContext.Entities.SingleOrDefault(e => e.ProjectId == entity.ProjectId && e.PreventBundleConfigDeployment.Length > 0);
                if (firstEntity != null)
                    return ("BundleConfig deployment is not allowed on " + firstEntity.Name + ": " + entity.PreventBundleConfigDeployment);
            }
            if (deploymentOptions.AppRouter)
            {
                var firstEntity = DbContext.Entities.SingleOrDefault(e => e.ProjectId == entity.ProjectId && e.PreventAppRouterDeployment.Length > 0);
                if (firstEntity != null)
                    return ("AppRouter deployment is not allowed on " + firstEntity.Name + ": " + entity.PreventAppRouterDeployment);
            }
            if (deploymentOptions.ApiResource)
            {
                var firstEntity = DbContext.Entities.SingleOrDefault(e => e.ProjectId == entity.ProjectId && e.PreventApiResourceDeployment.Length > 0);
                if (firstEntity != null)
                    return ("ApiResource deployment is not allowed on " + firstEntity.Name + ": " + entity.PreventApiResourceDeployment);
            }

            #region model
            if (deploymentOptions.Model)
            {
                var path = Path.Combine(entity.Project.RootPath, "Models");
                if (!Directory.Exists(path))
                    return ("Models path does not exist");

                var code = codeGenerator.GenerateModel();
                if (code != string.Empty) File.WriteAllText(Path.Combine(path, entity.Name + ".cs"), code);

                // todo: move this to own deployment option
                if (!CreateAppDirectory(entity.Project, "common\\models", codeGenerator.GenerateTypeScriptModel(), entity.Name.ToLower() + ".model.ts"))
                    return ("App path does not exist");
            }
            #endregion

            #region dbcontext
            if (deploymentOptions.DbContext)
            {
                var path = Path.Combine(entity.Project.RootPath, "Models");
                if (!Directory.Exists(path))
                    return ("Models path does not exist");

                var code = codeGenerator.GenerateDbContext();
                if (code != string.Empty) File.WriteAllText(Path.Combine(path, "ApplicationDBContext_.cs"), code);
            }
            #endregion

            #region dto
            if (deploymentOptions.DTO)
            {
                var path = Path.Combine(entity.Project.RootPath, "Models\\DTOs");
                if (!Directory.Exists(path))
                    return ("DTOs path does not exist");

                var code = codeGenerator.GenerateDTO();
                if (code != string.Empty) File.WriteAllText(Path.Combine(path, entity.Name + "DTO.cs"), code);
            }
            #endregion

            #region enums
            if (deploymentOptions.Enums)
            {
                var path = Path.Combine(entity.Project.RootPath, "Models");
                if (!Directory.Exists(path))
                    return ("Models path does not exist");

                var code = codeGenerator.GenerateEnums();
                if (code != string.Empty) File.WriteAllText(Path.Combine(path, "Enums.cs"), code);
            }
            #endregion

            #region settings
            if (deploymentOptions.SettingsDTO)
            {
                var path = Path.Combine(entity.Project.RootPath, "Models\\DTOs");
                if (!Directory.Exists(path))
                    return ("DTOs path does not exist");

                var code = codeGenerator.GenerateSettingsDTO();
                if (code != string.Empty) File.WriteAllText(Path.Combine(path, "SettingsDTO_.cs"), code);
            }
            #endregion

            #region settings dto
            if (deploymentOptions.SettingsDTO)
            {
                var path = Path.Combine(entity.Project.RootPath, "Models\\DTOs");
                if (!Directory.Exists(path))
                    return ("DTOs path does not exist");

                var code = codeGenerator.GenerateSettingsDTO();
                if (code != string.Empty) File.WriteAllText(Path.Combine(path, "SettingsDTO_.cs"), code);
            }
            #endregion

            #region controller
            if (deploymentOptions.Controller)
            {
                var path = Path.Combine(entity.Project.RootPath, "Controllers");
                if (!Directory.Exists(path))
                    return ("Controllers path does not exist");

                var code = codeGenerator.GenerateController();
                if (code != string.Empty) File.WriteAllText(Path.Combine(path, entity.PluralName + "Controller.cs"), code);
            }
            #endregion

            #region list html
            if (deploymentOptions.ListHtml)
            {
                if (!CreateAppDirectory(entity.Project, entity.PluralName, codeGenerator.GenerateListHtml(), entity.Name.ToLower() + ".list.component.html"))
                    return ("App path does not exist");
            }
            #endregion

            #region list typescript
            if (deploymentOptions.ListTypeScript)
            {
                if (!CreateAppDirectory(entity.Project, entity.PluralName, codeGenerator.GenerateListTypeScript(), entity.Name.ToLower() + ".list.component.ts"))
                    return ("App path does not exist");
            }
            #endregion

            #region edit html
            if (deploymentOptions.EditHtml)
            {
                if (!CreateAppDirectory(entity.Project, entity.PluralName, codeGenerator.GenerateEditHtml(), entity.Name.ToLower() + ".edit.component.html"))
                    return ("App path does not exist");
            }
            #endregion

            #region edit typescript
            if (deploymentOptions.EditTypeScript)
            {
                if (!CreateAppDirectory(entity.Project, entity.PluralName, codeGenerator.GenerateEditTypeScript(), entity.Name.ToLower() + ".edit.component.ts"))
                    return ("App path does not exist");
            }
            #endregion

            #region api resource
            if (deploymentOptions.ApiResource)
            {
                if (!CreateAppDirectory(entity.Project, "common\\services", codeGenerator.GenerateApiResource(), entity.Name.ToLower() + ".service.ts"))
                    return ("App path does not exist");
            }
            #endregion

            // todo: rename
            #region bundleconfig
            if (deploymentOptions.BundleConfig)
            {
                var path = entity.Project.RootPath + @"ClientApp\src\app\";

                var code = codeGenerator.GenerateBundleConfig();
                if (code != string.Empty) File.WriteAllText(Path.Combine(path, "generated.module.ts"), code);
            }
            #endregion

            #region app router
            if (deploymentOptions.AppRouter)
            {
                var path = entity.Project.RootPath + @"ClientApp\src\app\";

                var code = codeGenerator.GenerateAppRouter();
                if (code != string.Empty) File.WriteAllText(Path.Combine(path, "generated.routes.ts"), code);
            }
            #endregion

            #region app-select html
            if (deploymentOptions.AppSelectHtml)
            {
                if (!CreateAppDirectory(entity.Project, entity.PluralName, codeGenerator.GenerateAppSelectHtml(), entity.Name.ToLower() + ".select.component.html"))
                    return ("App path does not exist");
            }
            #endregion

            #region app-select typescript
            if (deploymentOptions.AppSelectTypeScript)
            {
                if (!CreateAppDirectory(entity.Project, entity.PluralName, codeGenerator.GenerateAppSelectTypeScript(), entity.Name.ToLower() + ".select.component.ts"))
                    return ("App path does not exist");
            }
            #endregion

            #region select modal html
            if (deploymentOptions.SelectModalHtml)
            {
                if (!CreateAppDirectory(entity.Project, entity.PluralName, codeGenerator.GenerateSelectModalHtml(), entity.Name.ToLower() + ".modal.component.html"))
                    return ("App path does not exist");
            }
            #endregion

            #region select modal typescript
            if (deploymentOptions.SelectModalTypeScript)
            {
                if (!CreateAppDirectory(entity.Project, entity.PluralName, codeGenerator.GenerateSelectModalTypeScript(), entity.Name.ToLower() + ".modal.component.ts"))
                    return ("App path does not exist");
            }
            #endregion

            return null;
        }

        private static bool CreateAppDirectory(Project project, string directoryName, string code, string fileName)
        {
            if (code == string.Empty) return true;

            var path = Path.Combine(project.RootPath, @"ClientApp\src\app");
            if (!Directory.Exists(path))
                return false;
            path = Path.Combine(path, directoryName.ToLower());
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            File.WriteAllText(Path.Combine(path, fileName), code);
            return true;
        }
    }

    public class DeploymentOptions
    {
        public bool Model { get; set; }
        public bool Enums { get; set; }
        public bool DTO { get; set; }
        public bool SettingsDTO { get; set; }
        public bool DbContext { get; set; }
        public bool Controller { get; set; }
        public bool BundleConfig { get; set; }
        public bool AppRouter { get; set; }
        public bool ApiResource { get; set; }
        public bool ListHtml { get; set; }
        public bool ListTypeScript { get; set; }
        public bool EditHtml { get; set; }
        public bool EditTypeScript { get; set; }
        public bool AppSelectHtml { get; set; }
        public bool AppSelectTypeScript { get; set; }
        public bool SelectModalHtml { get; set; }
        public bool SelectModalTypeScript { get; set; }
    }

}
