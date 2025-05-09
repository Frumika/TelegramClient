using Telegram.Td;
using Telegram.Td.Api;
using Td = Telegram.Td;
using TdApi = Telegram.Td.Api;

namespace TDLib;

public class Wrapper
{
    private const int ApiId = 20346937;
    private const string ApiHash = "f6aad01d4a9256f9ff4fe09863b948c9";

    private static Client _client = null!;

    private static readonly Td.ClientResultHandler Handler = new DefaultHandler();

    private static TdApi.AuthorizationState _authorizationState = null!;
    private static volatile bool _haveAuthorization = false;
    private static volatile bool _needQuit = false;
    private static volatile bool _canQuit = false;

    private static volatile AutoResetEvent _gotAuthorization = new AutoResetEvent(false);

    private static readonly string NewLine = Environment.NewLine;

    private static readonly string CommandsLine =
        "Enter command (gc <chatId> - GetChat, me - GetMe, sm <chatId> <message> - SendMessage, lo - LogOut, r - Restart, q - Quit): ";

    private static volatile string _currentPrompt = null;

    public Wrapper()
    {
        Td.Client.Execute(new TdApi.SetLogVerbosityLevel(0));

        new Thread(() =>
        {
            Thread.CurrentThread.IsBackground = true;
            Td.Client.Run();
        }).Start();

        _client = CreateTdClient();

        // test Client.Execute
        Handler.OnResult(Td.Client.Execute(
            new TdApi.GetTextEntities("@telegram /test_command https://telegram.org telegram.me @gif @test")));

        // main loop
        while (!_needQuit)
        {
            // await authorization
            _gotAuthorization.Reset();
            _gotAuthorization.WaitOne();

            _client.Send(new TdApi.LoadChats(null, 100), Handler); // preload main chat list
            while (_haveAuthorization)
            {
                GetCommand();
            }
        }

        while (!_canQuit)
        {
            Thread.Sleep(1);
        }
    }

    private static Td.Client CreateTdClient()
    {
        return Td.Client.Create(new UpdateHandler());
    }

    private static void Print(string str)
    {
        if (_currentPrompt != null)
        {
            Console.WriteLine();
        }

        Console.WriteLine(str);
        if (_currentPrompt != null)
        {
            Console.Write(_currentPrompt);
        }
    }

    private static string ReadLine(string str)
    {
        Console.Write(str);
        _currentPrompt = str;
        var result = Console.ReadLine();
        _currentPrompt = null;
        return result;
    }

    private static void OnAuthorizationStateUpdated(TdApi.AuthorizationState authorizationState)
    {
        if (authorizationState != null)
        {
            _authorizationState = authorizationState;
        }

        if (_authorizationState is TdApi.AuthorizationStateWaitTdlibParameters)
        {
            TdApi.SetTdlibParameters request = new TdApi.SetTdlibParameters();

            request.DatabaseDirectory = "tdlib";
            request.UseMessageDatabase = true;
            request.UseSecretChats = true;
            request.ApiId = ApiId;
            request.ApiHash = ApiHash;
            request.SystemLanguageCode = "en";
            request.DeviceModel = "Desktop";
            request.ApplicationVersion = "1.0";

            _client.Send(request, new AuthorizationRequestHandler());
        }
        else if (_authorizationState is TdApi.AuthorizationStateWaitPhoneNumber)
        {
            string phoneNumber = ReadLine("Please enter phone number: ");
            _client.Send(new TdApi.SetAuthenticationPhoneNumber(phoneNumber, null), new AuthorizationRequestHandler());
        }
        else if (_authorizationState is TdApi.AuthorizationStateWaitEmailAddress)
        {
            string emailAddress = ReadLine("Please enter email address: ");
            _client.Send(new TdApi.SetAuthenticationEmailAddress(emailAddress), new AuthorizationRequestHandler());
        }
        else if (_authorizationState is TdApi.AuthorizationStateWaitEmailCode)
        {
            string code = ReadLine("Please enter email authentication code: ");
            _client.Send(new TdApi.CheckAuthenticationEmailCode(new TdApi.EmailAddressAuthenticationCode(code)),
                new AuthorizationRequestHandler());
        }
        else if (_authorizationState is TdApi.AuthorizationStateWaitOtherDeviceConfirmation state)
        {
            Console.WriteLine("Please confirm this login link on another device: " + state.Link);
        }
        else if (_authorizationState is TdApi.AuthorizationStateWaitCode)
        {
            string code = ReadLine("Please enter authentication code: ");
            _client.Send(new TdApi.CheckAuthenticationCode(code), new AuthorizationRequestHandler());
        }
        else if (_authorizationState is TdApi.AuthorizationStateWaitRegistration)
        {
            string firstName = ReadLine("Please enter your first name: ");
            string lastName = ReadLine("Please enter your last name: ");
            _client.Send(new TdApi.RegisterUser(firstName, lastName, false), new AuthorizationRequestHandler());
        }
        else if (_authorizationState is TdApi.AuthorizationStateWaitPassword)
        {
            string password = ReadLine("Please enter password: ");
            _client.Send(new TdApi.CheckAuthenticationPassword(password), new AuthorizationRequestHandler());
        }
        else if (_authorizationState is TdApi.AuthorizationStateReady)
        {
            _haveAuthorization = true;
            _gotAuthorization.Set();
        }
        else if (_authorizationState is TdApi.AuthorizationStateLoggingOut)
        {
            _haveAuthorization = false;
            Print("Logging out");
        }
        else if (_authorizationState is TdApi.AuthorizationStateClosing)
        {
            _haveAuthorization = false;
            Print("Closing");
        }
        else if (_authorizationState is TdApi.AuthorizationStateClosed)
        {
            Print("Closed");
            if (!_needQuit)
            {
                _client = CreateTdClient(); // recreate _client after previous has closed
            }
            else
            {
                _canQuit = true;
            }
        }
        else
        {
            Print("Unsupported authorization state:" + NewLine + _authorizationState);
        }
    }

    private static long GetChatId(string arg)
    {
        long chatId = 0;
        try
        {
            chatId = Convert.ToInt64(arg);
        }
        catch (FormatException)
        {
        }
        catch (OverflowException)
        {
        }

        return chatId;
    }

    private static void GetCommand()
    {
        string command = ReadLine(CommandsLine);
        string[] commands = command.Split(new char[] { ' ' }, 2);
        try
        {
            switch (commands[0])
            {
                case "gc":
                    _client.Send(new TdApi.GetChat(GetChatId(commands[1])), Handler);
                    break;
                case "me":
                    _client.Send(new TdApi.GetMe(), Handler);
                    break;
                case "sm":
                    string[] args = commands[1].Split(new char[] { ' ' }, 2);
                    SendMessage(GetChatId(args[0]), args[1]);
                    break;
                case "lo":
                    _haveAuthorization = false;
                    _client.Send(new TdApi.LogOut(), Handler);
                    break;
                case "r":
                    _haveAuthorization = false;
                    _client.Send(new TdApi.Close(), Handler);
                    break;
                case "q":
                    _needQuit = true;
                    _haveAuthorization = false;
                    _client.Send(new TdApi.Close(), Handler);
                    break;
                default:
                    Print("Unsupported command: " + command);
                    break;
            }
        }
        catch (IndexOutOfRangeException)
        {
            Print("Not enough arguments");
        }
    }

    private static void SendMessage(long chatId, string message)
    {
        // initialize reply markup just for testing
        TdApi.InlineKeyboardButton[] row =
        {
            new TdApi.InlineKeyboardButton("https://telegram.org?1", new TdApi.InlineKeyboardButtonTypeUrl()),
            new TdApi.InlineKeyboardButton("https://telegram.org?2", new TdApi.InlineKeyboardButtonTypeUrl()),
            new TdApi.InlineKeyboardButton("https://telegram.org?3", new TdApi.InlineKeyboardButtonTypeUrl())
        };
        TdApi.ReplyMarkup replyMarkup =
            new TdApi.ReplyMarkupInlineKeyboard(new TdApi.InlineKeyboardButton[][] { row, row, row });

        TdApi.InputMessageContent content =
            new TdApi.InputMessageText(new TdApi.FormattedText(message, null), null, true);
        _client.Send(new TdApi.SendMessage(chatId, 0, null, null, replyMarkup, content), Handler);
    }


    private class DefaultHandler : Td.ClientResultHandler
    {
        void Td.ClientResultHandler.OnResult(TdApi.BaseObject @object)
        {
            Print(@object.ToString());
        }
    }

    private class UpdateHandler : Td.ClientResultHandler
    {
        void Td.ClientResultHandler.OnResult(TdApi.BaseObject @object)
        {
            if (@object is TdApi.UpdateAuthorizationState)
            {
                OnAuthorizationStateUpdated((@object as TdApi.UpdateAuthorizationState).AuthorizationState);
            }
            else
            {
                Print("Unsupported update: " + @object);
            }
        }
    }

    private class AuthorizationRequestHandler : Td.ClientResultHandler
    {
        void Td.ClientResultHandler.OnResult(TdApi.BaseObject @object)
        {
            if (@object is TdApi.Error)
            {
                Print("Receive an error:" + NewLine + @object);
                OnAuthorizationStateUpdated(null); // repeat last action
            }
            else
            {
                // result is already received through UpdateAuthorizationState, nothing to do
            }
        }
    }
}