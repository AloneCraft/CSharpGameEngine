using SceneAuthoringDemo;

string result = DemoScenario.Run();
Console.WriteLine(result);

if (result != "OK")
{
    Environment.Exit(1);
}
