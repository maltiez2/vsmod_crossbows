using Vintagestory.API.Common;

namespace Crossbows;

internal class CrossbowsModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("Crossbows:Crossbow", typeof(CrossbowItem));
        api.RegisterItemClass("Crossbows:MagazineCrossbow", typeof(MagazineCrossbowItem));
    }
}
