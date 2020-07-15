using System;
using System.ComponentModel.DataAnnotations;

namespace WEB.Models
{
    public class ProjectDTO
    {
        [Required]
        public Guid ProjectId { get; set; }

        [DisplayFormat(ConvertEmptyStringToNull = false)]
        [MaxLength(50)]
        public string Name { get; set; }

        //[DisplayFormat(ConvertEmptyStringToNull = false)]
        //[MaxLength(250)]
        //public string RootPath { get; set; }

        [DisplayFormat(ConvertEmptyStringToNull = false)]
        [MaxLength(20)]
        public string Namespace { get; set; }

        [DisplayFormat(ConvertEmptyStringToNull = false)]
        [MaxLength(20)]
        public string AngularModuleName { get; set; }

        [DisplayFormat(ConvertEmptyStringToNull = false)]
        [MaxLength(20)]
        public string AngularDirectivePrefix { get; set; }

        [Required]
        public bool Bootstrap3 { get; set; }

        [MaxLength(50)]
        public string UrlPrefix { get; set; }

        [Required]
        public bool UseStringAuthorizeAttributes { get; set; }

        [DisplayFormat(ConvertEmptyStringToNull = false)]
        [MaxLength(20)]
        public string DbContextVariable { get; set; }

        [MaxLength(50)]
        public string UserFilterFieldName { get; set; }

        public string Notes { get; set; }

        [MaxLength(20)]
        public string RouteViewName { get; set; }

    }

    public partial class ModelFactory
    {
        public ProjectDTO Create(Project project)
        {
            if (project == null) return null;

            var projectDTO = new ProjectDTO();

            projectDTO.ProjectId = project.ProjectId;
            projectDTO.Name = project.Name;
            //projectDTO.RootPath = project.RootPath;
            projectDTO.Namespace = project.Namespace;
            projectDTO.AngularModuleName = project.AngularModuleName;
            projectDTO.AngularDirectivePrefix = project.AngularDirectivePrefix;
            projectDTO.Bootstrap3 = project.Bootstrap3;
            projectDTO.UrlPrefix = project.UrlPrefix;
            projectDTO.UserFilterFieldName = project.UserFilterFieldName;
            projectDTO.UseStringAuthorizeAttributes = project.UseStringAuthorizeAttributes;
            projectDTO.DbContextVariable = project.DbContextVariable;
            projectDTO.Notes = project.Notes;
            projectDTO.RouteViewName = project.RouteViewName;

            return projectDTO;
        }

        public void Hydrate(Project project, ProjectDTO projectDTO)
        {
            project.Name = projectDTO.Name;
            //project.RootPath = projectDTO.RootPath;
            project.Namespace = projectDTO.Namespace;
            project.AngularModuleName = projectDTO.AngularModuleName;
            project.AngularDirectivePrefix = projectDTO.AngularDirectivePrefix;
            project.Bootstrap3 = projectDTO.Bootstrap3;
            project.UrlPrefix = projectDTO.UrlPrefix;
            project.UserFilterFieldName = projectDTO.UserFilterFieldName;
            project.UseStringAuthorizeAttributes = projectDTO.UseStringAuthorizeAttributes;
            project.DbContextVariable = projectDTO.DbContextVariable;
            project.Notes = projectDTO.Notes;
            project.RouteViewName = projectDTO.RouteViewName;
        }
    }
}
