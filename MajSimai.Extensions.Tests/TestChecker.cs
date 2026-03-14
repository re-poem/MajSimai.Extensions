using MajSimai.Extensions.Checker;

namespace MajSimai.Extensions.Tests;


[TestClass]
public sealed class TestChecker
{
    [TestMethod]
    public void TestCheck()
    {
        string fumen =
@"(150){1},

{8}1,2,3,4,5,6,7,8,9,0,

";
        SimaiChecker.Check(fumen);
    }
}
