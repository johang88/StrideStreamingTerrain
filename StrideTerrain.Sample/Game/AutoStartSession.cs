using Stride.Core;
using Stride.Engine;

namespace StrideTerrain.Sample.Game;

public class AutoStartSession : StartupScript
{
    public override void Start()
    {
        base.Start();

        Services.GetSafeServiceAs<GameSessionSystem>().CreateNewSession();
    }
}
