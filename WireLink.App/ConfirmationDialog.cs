using System.Drawing.Drawing2D;

namespace WireLink.App;

internal sealed record ConfirmationPrompt(string Title, string Message, string ConfirmLabel, bool Destructive = false);

internal sealed class ConfirmationDialog : Form
{
    private readonly WindowPalette palette;
    private readonly bool dark;
    private readonly Font bodyFont = new("Microsoft YaHei UI", 10);
    private readonly Font headingFont = new("Microsoft YaHei UI", 14, FontStyle.Bold);

    public ConfirmationDialog(ConfirmationPrompt prompt, bool dark)
    {
        this.dark = dark;
        palette = WindowPalette.For(dark);
        Text = AccessibleName = prompt.Title;
        Font = bodyFont;
        BackColor = palette.Card;
        ForeColor = palette.Text;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(24);

        var layout = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            ColumnCount = 1, MinimumSize = new Size(392, 0), Margin = Padding.Empty
        };
        layout.Controls.Add(new Label
        {
            Text = prompt.Title, Font = headingFont, AutoSize = true, UseMnemonic = false,
            Margin = new Padding(0, 0, 0, 10)
        });
        layout.Controls.Add(new Label
        {
            Text = prompt.Message, AutoSize = true, UseMnemonic = false,
            MaximumSize = new Size(400, 0), Margin = new Padding(0, 0, 0, 22)
        });

        var cancel = new DialogButton("取消", DialogResult.Cancel, palette.Header, palette.Text, palette.Border, palette.Accent);
        var primary = prompt.Destructive ? Color.FromArgb(165, 54, 63) : Color.FromArgb(8, 123, 121);
        var confirm = new DialogButton(prompt.ConfirmLabel, DialogResult.OK, primary, Color.White, primary, palette.Accent);
        var actions = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Margin = Padding.Empty
        };
        actions.Controls.AddRange([cancel, confirm]);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        CancelButton = cancel;
        AcceptButton = cancel;
        ActiveControl = cancel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowAppearance.ApplyFrame(Handle, dark, palette);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(palette.Border);
        e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { headingFont.Dispose(); bodyFont.Dispose(); }
    }

    private sealed class DialogButton : Button
    {
        public DialogButton(string text, DialogResult result, Color fill, Color ink, Color border, Color focus)
        {
            Text = text;
            DialogResult = result;
            ForeColor = ink;
            BackColor = fill;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            AutoSize = true;
            MinimumSize = new Size(90, 38);
            Padding = new Padding(12, 4, 12, 4);
            Margin = new Padding(result == DialogResult.OK ? 8 : 0, 0, 0, 0);
            this.border = border;
            this.focus = focus;
        }

        private readonly Color border, focus;
        private bool hovered;

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = LogicalToDeviceUnits(7);
            var right = Width - 1;
            var bottom = Height - 1;
            using var shape = new GraphicsPath();
            shape.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
            shape.AddArc(right - radius * 2, 0, radius * 2, radius * 2, 270, 90);
            shape.AddArc(right - radius * 2, bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            shape.AddArc(0, bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            shape.CloseFigure();
            using var background = new SolidBrush(hovered ? ControlPaint.Light(BackColor, .12f) : BackColor);
            using var outline = new Pen(Focused ? focus : border);
            e.Graphics.FillPath(background, shape);
            e.Graphics.DrawPath(outline, shape);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
