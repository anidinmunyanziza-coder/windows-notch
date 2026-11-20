using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Compact month calendar shown in the notch Home view when no media is
/// playing — the original NotchHomeView calendar. Today is highlighted; the
/// grid can be paged month to month. There is no event source wired on Windows
/// yet, so the side panel honestly shows "No events today".
/// </summary>
public partial class CalendarView : UserControl
{
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public CalendarView()
    {
        InitializeComponent();
        BuildWeekdayHeader();
        BuildMonth();
    }

    private void BuildWeekdayHeader()
    {
        WeekdayRow.Children.Clear();
        foreach (var d in new[] { "S", "M", "T", "W", "T", "F", "S" })
        {
            WeekdayRow.Children.Add(new TextBlock
            {
                Text = d,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x73)),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
    }

    private void BuildMonth()
    {
        MonthLabel.Text = _month.ToString("MMMM", CultureInfo.CurrentCulture);
        YearLabel.Text = _month.Year.ToString(CultureInfo.InvariantCulture);

        DaysGrid.Children.Clear();

        // Leading days from the previous month so the 1st lands on its weekday
        // (week starts Sunday, matching the original).
        var firstDowSunday = (int)_month.DayOfWeek; // Sun=0 … Sat=6
        var gridStart = _month.AddDays(-firstDowSunday);
        var today = DateTime.Today;

        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            var inMonth = date.Month == _month.Month;
            var isToday = date == today;

            // Stop after the last week that still contains this month (5 or 6 rows).
            if (i >= 35 && date.Month != _month.Month) break;

            var text = new TextBlock
            {
                Text = date.Day.ToString(CultureInfo.InvariantCulture),
                FontSize = 12,
                FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isToday
                    ? Brushes.White
                    : new SolidColorBrush(inMonth
                        ? Color.FromRgb(0xEE, 0xEE, 0xEE)
                        : Color.FromRgb(0x55, 0x55, 0x59)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var cell = new Border
            {
                Height = 20,
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Background = isToday
                    ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF))
                    : Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = text,
            };
            if (isToday) { cell.Width = 20; cell.HorizontalAlignment = HorizontalAlignment.Center; }

            DaysGrid.Children.Add(cell);
        }
    }

    private void OnPrev(object sender, RoutedEventArgs e) { _month = _month.AddMonths(-1); BuildMonth(); }
    private void OnNext(object sender, RoutedEventArgs e) { _month = _month.AddMonths(1); BuildMonth(); }
    private void OnToday(object sender, RoutedEventArgs e)
    {
        _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        BuildMonth();
    }
}
