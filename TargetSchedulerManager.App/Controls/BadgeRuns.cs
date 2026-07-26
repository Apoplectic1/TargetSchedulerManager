using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using TargetSchedulerManager.App.Models;

namespace TargetSchedulerManager.App.Controls;

/// <summary>
/// Paints the grid's Badges cell one token at a time (openspec badge-severity-color):
/// <c>local:BadgeRuns.Tokens="{x:Bind Badge}"</c> fills the TextBlock's <see cref="TextBlock.Inlines"/> with a
/// <see cref="Run"/> per token, coloured by <see cref="Badges.IsWarning"/> — warning amber vs. quiet
/// informative — so a row reading "mosaic · multi-plan" shows each token at its own severity instead of
/// promoting the whole cell to the louder one.
/// <para>Runs inside ONE TextBlock (not a StackPanel of TextBlocks) because a panel cannot ellipsis-trim: a
/// TextBlock trims across its inlines, so the 150 px column keeps its <c>TextTrimming</c> for free.</para>
/// <para>The grid is a virtualised ListView, so the handler clears the inlines before rebuilding — a recycled
/// container can never show the previous row's tokens. A recycled container receiving the SAME badge string
/// doesn't re-enter the callback and doesn't need to: its runs already say exactly that.</para>
/// </summary>
public static class BadgeRuns
{
    public static readonly DependencyProperty TokensProperty =
        DependencyProperty.RegisterAttached("Tokens", typeof(string), typeof(BadgeRuns),
            new PropertyMetadata(null, OnTokensChanged));

    public static void SetTokens(TextBlock target, string value) => target.SetValue(TokensProperty, value);
    public static string GetTokens(TextBlock target) => (string)target.GetValue(TokensProperty);

    private static void OnTokensChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text)
            return;

        text.Inlines.Clear();
        bool first = true;
        foreach ((string token, bool isWarning) in Badges.Split(e.NewValue as string))
        {
            if (!first)
                text.Inlines.Add(new Run { Text = Badges.Separator, Foreground = ThemeBrushes.Secondary });
            text.Inlines.Add(new Run
            {
                Text = token,
                Foreground = isWarning ? ThemeBrushes.CautionText : ThemeBrushes.Secondary,
            });
            first = false;
        }
    }
}
