using System.Text;
using RetroBoard.Contracts;

namespace RetroBoard.Api.Extensions;

/// <summary>Renders a board snapshot as Markdown for the §5.7 export — all columns' cards plus the
/// tracked action-item list, suitable for pasting into a wiki/ticket.</summary>
public static class MarkdownExtensions
{
    public static string ToMarkdown(this BoardStateResponse state)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {state.Name}");
        builder.AppendLine();

        foreach (var column in state.Columns)
        {
            builder.AppendLine($"## {column.Title}");
            builder.AppendLine();

            if (column.Cards.Count == 0)
            {
                builder.AppendLine("_No cards._");
            }

            foreach (var card in column.Cards)
            {
                AppendCard(builder, card, depth: 0);
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Action Items");
        builder.AppendLine();

        if (state.ActionItems.Count == 0)
        {
            builder.AppendLine("_No action items._");
        }

        foreach (var item in state.ActionItems)
        {
            var assignee = string.IsNullOrWhiteSpace(item.AssigneeName) ? string.Empty : $" — {item.AssigneeName}";
            var dueDate = item.DueDate is { } due ? $" (due {due:yyyy-MM-dd})" : string.Empty;
            builder.AppendLine($"- [ ] {item.Text}{assignee}{dueDate}");
        }

        return builder.ToString();
    }

    private static void AppendCard(StringBuilder builder, CardResponse card, int depth)
    {
        var indent = new string(' ', depth * 2);
        var votes = card.VoteCount is { } count ? $" — {count} vote{(count == 1 ? "" : "s")}" : string.Empty;
        builder.AppendLine($"{indent}- {card.Text} ({card.AuthorName}){votes}");

        foreach (var stacked in card.StackedCards)
        {
            AppendCard(builder, stacked, depth + 1);
        }
    }
}
