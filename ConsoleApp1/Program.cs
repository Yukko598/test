using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Polling;

class Program
{
    private static ITelegramBotClient? _botClient;

    private static readonly List<string> BannedWords = new List<string>
    {
        "плохой",
        "Квадробер",
        "Пирог",
        "Хобехорсер",
        "Электросамокат",
        "радость"
    };

    private static readonly Dictionary<long, int> UserWarnings = new();
    private static readonly Dictionary<long, UserStats> UserLevels = new();

    // ID групп для пересылки сообщений
    private static readonly long SourceChatId = -1002465687453; // Замените на ID группы-источника
    private static readonly long TargetChatId = -1002405135994; // Замените на ID группы-приёмника

    static async Task Main()
    {
        _botClient = new TelegramBotClient("7435627036:AAGjuuOygDzXYQ_2HJtkG2xMQL_JFKDhoVM");

        using var cts = new CancellationTokenSource();

        Console.WriteLine("Бот запускается...");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>(),
        };

        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken: cts.Token
        );

        var me = await _botClient.GetMeAsync();
        Console.WriteLine($"{me.FirstName} запущен!");

        await Task.Delay(-1);
    }

    private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken token)
    {
        Console.WriteLine($"Ошибка: {exception.Message}");
        return Task.CompletedTask;
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.Type == UpdateType.Message && update.Message != null)
            {
                var message = update.Message;
                var chatId = message.Chat.Id;
                var userId = message.From?.Id ?? 0;
                var userName = message.From?.FirstName ?? "Пользователь";
                var messageId = message.MessageId;

                Console.WriteLine($"Получено сообщение: {message.Text} из чата {chatId}");

                // Пересылка сообщений из группы-источника в группу-приёмник
                if (chatId == SourceChatId)
                {
                    Console.WriteLine("Сообщение из группы-источника, пересылаю в группу-приёмник...");
                    await botClient.ForwardMessageAsync(
                        chatId: TargetChatId,      // Группа-приёмник
                        fromChatId: SourceChatId,  // Группа-источник
                        messageId: messageId,
                        cancellationToken: cancellationToken
                    );
                }

                // Обработка команды /help
                if (message.Text?.ToLower() == "/help")
                {
                    string helpMessage = "Вот что я могу:\n1. Удаляю сообщения с запрещёнными словами.\n" +
                                         "2. Удаляю стикеры.\n3. Предупреждаю нарушителей (3 предупреждения = бан).\n" +
                                         "4. Показываю статистику (/stats).\n5. Пересылаю сообщения между группами.";

                    await botClient.SendTextMessageAsync(chatId, helpMessage, cancellationToken: cancellationToken);
                }

                // Добавляем статистику
                await AddUserStats(userId, userName, chatId, botClient, cancellationToken);

                // Удаление сообщений с запрещёнными словами
                if (!string.IsNullOrEmpty(message.Text) && ContainsBannedWords(message.Text))
                {
                    await botClient.DeleteMessageAsync(chatId, messageId);
                    Console.WriteLine($"Сообщение {messageId} удалено.");
                    await SendWarning(botClient, chatId, userId, "Вы использовали запрещённое слово.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка обработки сообщения: {ex.Message}");
        }
    }

    private static bool ContainsBannedWords(string text)
    {
        return BannedWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task SendWarning(ITelegramBotClient botClient, long chatId, long userId, string warningMessage)
    {
        if (!UserWarnings.ContainsKey(userId))
        {
            UserWarnings[userId] = 0;
        }

        UserWarnings[userId]++;
        if (UserWarnings[userId] >= 3)
        {
            await botClient.BanChatMemberAsync(chatId, userId);
            await botClient.SendTextMessageAsync(chatId, $"Пользователь {userId} был заблокирован за нарушения.", cancellationToken: default);
            UserWarnings.Remove(userId);
        }
        else
        {
            await botClient.SendTextMessageAsync(chatId, $"⚠ Предупреждение {UserWarnings[userId]}/3: {warningMessage}", cancellationToken: default);
        }
    }

    private static async Task AddUserStats(long userId, string userName, long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
    {
        if (!UserLevels.ContainsKey(userId))
        {
            UserLevels[userId] = new UserStats { UserName = userName };
        }
        var userStats = UserLevels[userId];
        userStats.MessageCount++;
        userStats.Experience += 10;

        if (userStats.Experience >= userStats.GetRequiredExperience())
        {
            userStats.Level++;
            userStats.Experience = 0;

            await botClient.SendTextMessageAsync(chatId, $"🎉 {userName}, поздравляем! У вас новый уровень!", cancellationToken: cancellationToken);
        }
    }
}

public class UserStats
{
    public string UserName { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public int Experience { get; set; } = 0;
    public int MessageCount { get; set; } = 0;

    public int GetRequiredExperience() => Level * 100;
}
