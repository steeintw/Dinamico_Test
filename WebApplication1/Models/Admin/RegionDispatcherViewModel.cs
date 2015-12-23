using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace WebApplication1.Models.Admin
{
    public class RegionDispatcherViewModel
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public bool IncludeChildren { get; set; }
        public List<LanguageOption> AvailableLanguages { get; set; }
    }

    public class LanguageOption
    {
        public string LanguageTitle { get; set; }
        public string CultureCode { get; set; }
        public int ItemID { get; set; }
        public bool Checked { get; set; }
    }

    public class RegionDispatcherPostModel
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public bool IncludeChildren { get; set; }
        public List<int> LangIds { get; set; }
    }
}