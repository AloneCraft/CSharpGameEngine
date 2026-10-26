using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
[SolutionTreeFilterProvider("521655e5-0d1b-4255-8086-1039c5c2e3da",AssetWorkspaceController.FilterCommandId)]
public sealed class AssetSolutionExplorerFilter:HierarchyTreeFilterProvider {
    private readonly IVsHierarchyItemCollectionProvider Collections;
    [ImportingConstructor]
    public AssetSolutionExplorerFilter(IVsHierarchyItemCollectionProvider Collections)=>this.Collections=Collections;
    protected override HierarchyTreeFilter CreateFilter()=>new AssetFilter(this.Collections);
    private sealed class AssetFilter(IVsHierarchyItemCollectionProvider Collections):HierarchyTreeFilter {
        protected override async Task<IReadOnlyObservableSet>GetIncludedItemsAsync(IEnumerable<IVsHierarchyItem>RootItems){
            var Root=RootItems.First();
            var Items=await Collections.GetDescendantsAsync(Root.HierarchyIdentity.NestedHierarchy,this.CancellationToken);
            return await Collections.GetFilteredHierarchyItemsAsync(Items,static Item=>
                AssetWorkspaceClassification.Classify(Item.CanonicalName??Item.Text) is not null
                &&!AssetWorkspaceClassification.IsExcluded(Item.CanonicalName??Item.Text),this.CancellationToken);
        }
    }
}
