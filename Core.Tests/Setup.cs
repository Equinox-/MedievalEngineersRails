using System.IO;
using System.Text;
using Equinox76561198048419394.RailSystem.Util;
using Medieval;
using NUnit.Framework;
using Sandbox.Game.WorldEnvironment;
using VRage.Components;
using VRage.Game.Components;
using VRage.Logging;
using VRage.Meta;

namespace Equinox76561198048419394.RailSystem.Tests
{
    [SetUpFixture]
    public class Setup
    {
        [OneTimeSetUp]
        public void Init()
        {
            MyLog.Default = new MyLog();
            MyLog.Default.Init(Path.GetFullPath("bin/log.txt"), new StringBuilder("unit-test"));
            MyMetadataSystem.LoadAssemblies(
                typeof(MyComponentFactory).Assembly,
                typeof(MyEntityComponent).Assembly,
                typeof(MyEnvironmentSector).Assembly,
                typeof(MyMedievalGame).Assembly,
                typeof(RootEntityRef).Assembly);
        }
    }
}