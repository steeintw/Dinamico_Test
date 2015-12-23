using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using N2.Edit;
using N2;
using N2.Web;
using WebApplication1.Models.Admin;
using WebApplication1.Models;
using System.Text.RegularExpressions;
using N2.Edit.Versioning;
using N2.Plugin;
using N2.Security;
using Dinamico.Models;

namespace WebApplication1.Controllers
{    
    [NavigationLinkPlugin("Dispatch","dispatch", "{{ContextMenu.appendSelection('/RegionDispatcher/')}}", Targets.Preview, "{ManagementUrl}/Resources/icons/arrow_out.png", 70,
        GlobalResourceClassName = "Navigation",         
        RequiredPermission = Permission.Write)]
    [Authorize(Roles = "Administrators")]    
    public class RegionDispatcherController : Controller
    {
        N2.Engine.IEngine engine;
        public N2.Engine.IEngine Engine
        {
            get { return engine ?? (engine = N2.Context.Current); }
            set { engine = value; }
        }

        SelectionUtility selection;
        public SelectionUtility Selection
        {
            get { return selection ?? (selection = new SelectionUtility(this.HttpContext, Engine)); }
            set { selection = value; }
        }
        //
        // GET: /Admin/RegionDispatcher/

        public ActionResult Index(string selected)
        {            
            var currentItem = Context.Current.Resolve<Navigator>().Navigate(selected);
            var startPage = N2.Content.Traverse.ClosestStartPage(currentItem);
            var langItems = N2.Content.Traverse.Siblings(startPage).Where(x => x.ID != startPage.ID);
            var languageIntersectionPath = N2.Content.Traverse.RootPage.Children[0].Path;
            var currentItemPath = currentItem.Path;
            if (languageIntersectionPath.Length > 1)
                currentItemPath = currentItem.Path.Remove(0, languageIntersectionPath.Length - 1);
            RegionDispatcherViewModel model = new RegionDispatcherViewModel();
            model.Name = currentItem.Name;
            model.Title = currentItem.Title;
            model.Url = currentItemPath;
            model.AvailableLanguages = new List<LanguageOption>();
            langItems.FirstOrDefault(x =>
            {
                var st = x as StartPage;
                model.AvailableLanguages.Add(new LanguageOption
                {
                    ItemID = x.ID,
                    Checked = true,
                    CultureCode = st.LanguageCode,
                    LanguageTitle = st.LanguageTitle
                });
                return false;
            });
            return View(model);
        }

        [HttpPost]
        public ActionResult Index(RegionDispatcherPostModel model)
        {
            if (ModelState.IsValid)
            {
                var currentItem = Context.Current.Resolve<IUrlParser>().Parse(model.Url);
                var currentItemName = currentItem.Name;
                var currentItemParent = currentItem.Parent;
                var currentItemParentName = currentItemParent.Name;
                var level = N2.Content.Traverse.LevelOf(currentItem);
                //Try get related parent node of current node in each language
                var targetLanguages = new N2.Collections.ItemList();
                var translationKey = currentItem.TranslationKey ?? currentItem.ID;
                var navUrl = N2.Context.Current.ManagementPaths.GetNavigationUrl(currentItem);
                var NodeAdapter = N2.Context.Current.Resolve<N2.Engine.IContentAdapterProvider>().ResolveAdapter<NodeAdapter>(currentItem);
                var previewUrl = NodeAdapter.GetPreviewUrl(currentItem);
                var permission = NodeAdapter.GetMaximumPermission(currentItem);
                var nodePath = currentItem.Path;
                var versionManager = N2.Context.Current.Resolve<IVersionManager>();
                var langGateway = N2.Context.Current.Resolve<N2.Engine.Globalization.ILanguageGateway>();
                //var persister = N2.Context.Current.Resolve<N2.Persistence.IPersister>();
                var persister = N2.Context.Current.Persister;
                try
                {
                    foreach (var langId in model.LangIds)
                    {                       
                        var st = persister.Get(langId);
                        var relativeItem = UpdateRelatedItemRecursive(st, currentItem, model.IncludeChildren, versionManager, persister, langGateway);
                        AssociateRecursive(relativeItem, currentItem, model.IncludeChildren, langGateway);                        
                    }
                    return Json(new { result = true, navUrl = navUrl, previewUrl = previewUrl, path = nodePath, permission = permission });
                }
                catch (Exception ex)
                {
                    return Json(new { result = false, navUrl = navUrl, previewUrl = previewUrl, path = nodePath, permission = permission, message = ex.Message });
                }
            }
            return Json(new { result = false, message = "Invalid ModelState"});
        }



        /// <summary>
        /// 更新特定語系的相關聯ContentItem 及其Children
        /// </summary>
        /// <param name="item"></param>
        private ContentItem UpdateRelatedItemRecursive(ContentItem targetItem, ContentItem sourceItem, bool includeChildren, IVersionManager versioner, N2.Persistence.IPersister persister, N2.Engine.Globalization.ILanguageGateway gateway)
        {
            var partFilter = new N2.Collections.PartFilter();
            var pageFilter = new N2.Collections.PageFilter();
            ContentItem relativeParent;
            //N2.Collections.ItemList associateItems = new N2.Collections.ItemList();
            // associateItems.Add(sourceItem);
            int? translationKey = null;
            translationKey = sourceItem.TranslationKey ?? sourceItem.ID;
            if (translationKey.HasValue && translationKey.Value == 0)
                translationKey = null;
            var relativeItem = TryGetRelatedItem(targetItem, sourceItem.Path, out relativeParent);

            var cloneItem = sourceItem.Clone(includeChildren);
            //cloneItem.TranslationKey = translationKey;

            //如果只複製本頁,只把非page的children複製過來
            if (!includeChildren)
            {
                var childParts = sourceItem.GetChildPartsUnfiltered();
                foreach (var part in childParts)
                {
                    var clonePart = part.Clone(true);
                    clonePart.Parent = cloneItem;
                    cloneItem.Children.Add(clonePart);
                }
            }
            //沒有找到時，新增
            if (relativeItem == null)
            {
                cloneItem.Parent = relativeParent;                
                persister.Save(cloneItem);
                //AssociateRecursive(cloneItem, sourceItem, includeChildren, gateway);
                // associateItems.Add(cloneItem);
                return cloneItem;
            }
            else
            {
                //更新目前Item
                //relativeItem.TranslationKey = cloneItem.TranslationKey; // don't know why.. not copied when using IVersionManager.ReplaceVersion()
                //StartPage的特殊判斷,不更新Name,Type,LanguageCode
                if (cloneItem.GetContentType() == typeof(StartPage))
                {
                    cloneItem.Name = relativeItem.Name;
                    cloneItem.Title = relativeItem.Title;
                    ((StartPage)cloneItem).LanguageCode = relativeItem.GetDetail<string>("LanguageCode", "en");
                    
                }

                //Page Title不更新
                cloneItem.Title = relativeItem.Title;
                persister.Save(cloneItem);

                var updatedItem = versioner.ReplaceVersion(relativeItem, cloneItem, false);
                //associateItems.Add(updatedItem);
                //AssociateRecursive(updatedItem, sourceItem, false, gateway);

                var partsToBeReplaced = updatedItem.GetChildPartsUnfiltered();

                foreach (var cr in partsToBeReplaced)
                    persister.Delete(cr);

                //replace child parts                            
                cloneItem.GetChildPartsUnfiltered().FirstOrDefault(x =>
                {
                    x.Parent = updatedItem;
                    persister.Save(x);
                    return false;
                });
                cloneItem.Children.Clear();
                persister.Delete(cloneItem);

                //update child pages recursively
                if (includeChildren)
                {
                    var childClonePages = sourceItem.GetChildPagesUnfiltered();
                    var childTargetPages = relativeItem.GetChildPagesUnfiltered();
                    //刪除在target有但source中不存在的
                    childTargetPages.FirstOrDefault(x =>
                    {
                        var chker = childClonePages.SingleOrDefault(y => y.Name.Equals(x.Name, StringComparison.CurrentCultureIgnoreCase));
                        if (chker == null)
                            persister.Delete(x);
                        return false;
                    });
                    childClonePages.FirstOrDefault(x =>
                    {
                        UpdateRelatedItemRecursive(relativeItem, x, includeChildren, versioner, persister, gateway);
                        return false;
                    });
                }
                updatedItem.State = ContentState.Published;
                persister.Save(updatedItem);
                return updatedItem;
            }

            //try
            //{
            //    gateway.Associate(associateItems);
            //}
            //catch (N2Exception ex)
            //{
            //    N2.Context.Current.Resolve<IErrorNotifier>().Notify(ex);                
            //}
        }

        public void AssociateRecursive(ContentItem A, ContentItem B, bool includeChildren, N2.Engine.Globalization.ILanguageGateway gateway)
        {
            N2.Collections.ItemList associateItems = new N2.Collections.ItemList();
            try
            {
                if (A.GetContentType() != typeof(StartPage)) // cannot associate language root.
                {
                    associateItems.Add(A);
                    associateItems.Add(B);
                    gateway.Associate(associateItems);
                }
                if (includeChildren)
                {
                    var childrenOfA = A.GetChildPagesUnfiltered();
                    var childrenOfB = B.GetChildPagesUnfiltered();
                    foreach (var child in childrenOfA)
                    {
                        ContentItem relativeParent = null;
                        var matchedItemOfB = TryGetRelatedItem(B, child.Path, out relativeParent);
                        if (matchedItemOfB != null)
                            AssociateRecursive(child, matchedItemOfB, includeChildren, gateway);
                    }
                }
            }
            catch (N2Exception ex)
            {
                N2.Context.Current.Resolve<IErrorNotifier>().Notify(ex);
            }

        }

        private ContentItem TryGetRelatedItem(ContentItem initialPage, string pathToCompare, out ContentItem relativeParent)
        {
            var sIndex = initialPage.Path.NthIndexOf("/", 3);
            if (sIndex < 0)
                throw new InvalidOperationException(String.Format("TryGetRelatedItem() failed! invalid initialPage path for \"{0}\"", initialPage.Path));

            var processedInitialPagePath = initialPage.Path.Substring(sIndex);

            var cIndex = pathToCompare.NthIndexOf("/", 3);
            if (cIndex < 0)
                throw new InvalidOperationException(String.Format("TryGetRelatedItem() failed! invalid pathToCompare for \"{0}\"", pathToCompare));
            var processedPathToCompare = pathToCompare.Substring(cIndex);// 去除initialPage的相對路徑
            if (!processedPathToCompare.StartsWith(processedInitialPagePath))
                throw new InvalidOperationException("TryGetRelatedItem failed! pathToCompare must under initialPage!");
            //if Paths are equal, return ;
            if (processedInitialPagePath.Equals(processedPathToCompare, StringComparison.CurrentCultureIgnoreCase))
            {
                relativeParent = initialPage.Parent;
                return initialPage;
            }
            var relativePath = processedPathToCompare.Substring(processedInitialPagePath.Length); // this method may not be good enough though...
            var pathSegs = relativePath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var currentItem = initialPage;
            ContentItem relativeItem = null;
            relativeParent = null;

            int dep = 0;
            while (dep < pathSegs.Length)
            {
                var child = currentItem.GetChild(pathSegs[dep]);
                if (child == null)
                {
                    if (dep < pathSegs.Length - 1)
                        throw new InvalidOperationException(String.Format("the node's relative parent does not exist in region - {0}!", initialPage.Name));

                    relativeParent = currentItem;
                    relativeItem = null;
                    break;
                }
                relativeParent = currentItem;
                relativeItem = currentItem = child;
                dep++;
            };
            return relativeItem;
        }
    }

    /// <summary>
    /// String Helper
    /// </summary>
    public static class StringExtender
    {
        public static int NthIndexOf(this string target, string value, int n)
        {
            Match m = Regex.Match(target, "((" + value + ").*?){" + n + "}");

            if (m.Success)
                return m.Groups[2].Captures[n - 1].Index;
            else
                return -1;
        }
    }
}
