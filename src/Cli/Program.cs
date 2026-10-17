using System;
using System.Text;
using System.Web.Script.Serialization;

namespace ArknightsSystemCursor
{
    internal static class CliProgram
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            JavaScriptSerializer json = new JavaScriptSerializer();
            try
            {
                string command = "help";
                if (args.Length > 0)
                {
                    command = args[0].ToLowerInvariant();
                }
                string? gameDirectory = null;
                string? stateDirectory = null;
                for (int index = 1; index < args.Length; index++)
                {
                    if (args[index] == "--game-dir" && index + 1 < args.Length)
                    {
                        gameDirectory = args[++index];
                    }
                    else if (args[index] == "--state-dir" && index + 1 < args.Length)
                    {
                        stateDirectory = args[++index];
                    }
                    else if (args[index] == "--json")
                    {
                        // Output is always JSON; the flag is accepted for clarity in agent commands.
                    }
                    else
                    {
                        throw new ArgumentException("未知或缺少参数值：" + args[index]);
                    }
                }
                CursorService service = new CursorService(stateDirectory);
                object output;
                if (command == "locate")
                {
                    output = new { Success = true, Directories = GameLocator.Find() };
                }
                else if (command == "status" || command == "enable" || command == "restore")
                {
                    if (gameDirectory != null)
                    {
                        if (command == "status")
                        {
                            output = new { Success = true, Status = service.Inspect(gameDirectory) };
                        }
                        else if (command == "enable")
                        {
                            output = service.Enable(gameDirectory);
                        }
                        else
                        {
                            output = service.Restore(gameDirectory);
                        }
                    }
                    else
                    {
                        throw new ArgumentException("请提供 --game-dir，并指定包含 Arknights.exe 的目录。");
                    }
                }
                else if (command == "help" || command == "--help" || command == "-h")
                {
                    output = new { Success = true, Usage = "ArknightsSystemCursor.Cli.exe <locate|status|enable|restore> --game-dir <path> [--json] [--state-dir <path>]", Version = typeof(CliProgram).Assembly.GetName().Version.ToString(3) };
                }
                else
                {
                    throw new ArgumentException("未知命令：" + command);
                }
                Console.WriteLine(json.Serialize(output));
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine(json.Serialize(new { Success = false, Error = error.Message }));
                return 1;
            }
        }
    }
}
