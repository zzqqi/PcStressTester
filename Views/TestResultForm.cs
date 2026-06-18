using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PcStressTester.Views;

public sealed class TestResultForm : Form
{
    public TestResultForm(string title, string summary, IReadOnlyList<(string Label, string Value)> metrics, Color accentColor)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(12, 17, 26);
        ForeColor = Color.White;
        ClientSize = new Size(520, 420);
        Padding = new Padding(0);

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(12, 17, 26),
            Padding = new Padding(24)
        };

        var contentPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        var titleLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(450, 0),
            Text = title,
            Font = new Font("Segoe UI", 17f, FontStyle.Bold),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 0, 8)
        };

        var summaryLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(450, 0),
            Text = summary,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
            ForeColor = accentColor,
            Margin = new Padding(0, 0, 0, 18)
        };

        var metricsPanel = new FlowLayoutPanel
        {
            Width = 450,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        foreach (var metric in metrics)
        {
            metricsPanel.Controls.Add(CreateMetricCard(metric.Label, metric.Value));
        }

        var okButton = new Button
        {
            Text = "Закрыть",
            Size = new Size(140, 40),
            FlatStyle = FlatStyle.Flat,
            BackColor = accentColor,
            ForeColor = Color.FromArgb(8, 12, 18),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        okButton.FlatAppearance.BorderSize = 0;
        okButton.Margin = new Padding(0, 12, 0, 0);
        okButton.Click += (_, _) => Close();

        contentPanel.Controls.Add(titleLabel);
        contentPanel.Controls.Add(summaryLabel);
        contentPanel.Controls.Add(metricsPanel);
        contentPanel.Controls.Add(okButton);
        root.Controls.Add(contentPanel);
        Controls.Add(root);
    }

    public static void ShowResult(IWin32Window? owner, string title, string summary, IReadOnlyList<(string Label, string Value)> metrics, Color accentColor)
    {
        using var form = new TestResultForm(title, summary, metrics, accentColor);
        if (owner is null)
        {
            form.StartPosition = FormStartPosition.CenterScreen;
            form.ShowDialog();
            return;
        }

        form.StartPosition = FormStartPosition.CenterParent;
        form.ShowDialog(owner);
    }

    private static Control CreateMetricCard(string label, string value)
    {
        var card = new Panel
        {
            Size = new Size(450, 56),
            BackColor = Color.FromArgb(20, 28, 40),
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(14, 10, 14, 10)
        };

        var labelControl = new Label
        {
            AutoSize = true,
            Text = label,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(171, 184, 203),
            Location = new Point(14, 10)
        };

        var valueControl = new Label
        {
            AutoSize = true,
            Text = value,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(14, 28)
        };

        card.Controls.Add(labelControl);
        card.Controls.Add(valueControl);
        return card;
    }
}

