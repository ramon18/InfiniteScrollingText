using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Threading;

namespace InfiniteScrollingText
{
    public enum TextDrawMode {

        /// <summary>
        /// The default chooses the best performance with complete functionality :) (which is ExTextOut)
        /// </summary>
        Default,

        /// <summary>
        /// Use GDI+ render the text, this is the worst in terms of performance
        /// </summary>
        GdiPlus,

        /// <summary>
        /// Use GDI to render the text, this is a little better than GDI+ in terms of performance
        /// </summary>
        Gdi,

        /// <summary>
        /// Uses the PInvoke TextOut API, great performance but needs extra GDI call to fill background (impact performance a little, but better than "Gdi" mode)
        /// </summary>
        TextOut,

        /// <summary>
        /// Uses the PInvoke ExTextOut API, best performance without limitations! (clipped and opaque)
        /// </summary>
        ExtTextOut
    }

    public class InfiniteScrollableControl : Control
    {
        #region Circular Buffer for "infinite" rows
        class CircularBuffer<T>
        {
            private readonly int maxBufferSize;
            private int bufferSize;
            private T[] buffer;
            private int head;
            private int tail;
            private int virtualCount;

            public CircularBuffer(int maximumCapacity)
            {
                if (maximumCapacity < 0)
                    throw new ArgumentOutOfRangeException(nameof(maximumCapacity), "Must be positive");

                this.maxBufferSize = maximumCapacity;
            }

            public int Count { get; private set; }

            public int VirtualCount
            {
                get
                {
                    if (this.buffer == null)
                        return 0;
                    else
                        return this.virtualCount + Count;
                }
            }

            private void MakeBufferOrGrow()
            {
                if (this.buffer == null)
                {
                    this.bufferSize = Math.Min(this.maxBufferSize, 50);
                    this.head = this.bufferSize - 1;
                    this.buffer = new T[this.bufferSize];
                }
                else
                {
                    if (this.bufferSize < this.maxBufferSize)
                    {
                        this.bufferSize = Math.Min(this.maxBufferSize, this.bufferSize * 2);
                        Array.Resize(ref this.buffer, this.bufferSize);
                    }
                }
            }

            public T Add(T item)
            {
                MakeBufferOrGrow();

                this.head = (this.head + 1) % this.buffer.Length;
                var overwritten = this.buffer[this.head];
                this.buffer[this.head] = item;
                if (Count == this.buffer.Length)
                {
                    this.virtualCount++;
                    this.tail = (this.tail + 1) % this.buffer.Length;
                }
                else
                    ++Count;
                return overwritten;
            }

            public void Clear()
            {
                if (this.buffer != null)
                {
                    this.head = this.buffer.Length - 1;
                    this.tail = 0;
                }

                Count = 0;
            }

            public T this[int index]
            {
                get
                {
                    if (index < 0 || index >= Count)
                        throw new ArgumentOutOfRangeException(nameof(index));

                    return this.buffer[(this.tail + index) % this.buffer.Length];
                }
                set
                {
                    if (index < 0 || index >= Count)
                        throw new ArgumentOutOfRangeException(nameof(index));

                    MakeBufferOrGrow();
                    this.buffer[(this.tail + index) % this.buffer.Length] = value;
                }
            }
        }
        #endregion Circular Buffer for "infinite" rows

        #region Text Drawing
        abstract class TextDrawerBase : IDisposable
        {
            protected Graphics Graphics { get; }

            protected TextDrawerBase(Graphics g)
            {
                Graphics = g;
            }

            public virtual void Dispose()
            {
            }

            public abstract Size MeasureString(string text, Font font);

            public abstract void Draw(string text, Font font, Rectangle rowRect, SolidBrush textBrush,
                SolidBrush textBkBrush);
        }

        class TextDrawerGdiPlus : TextDrawerBase
        {
            private static readonly StringFormat StrFormat;

            static TextDrawerGdiPlus()
            {
                StrFormat = (StringFormat) StringFormat.GenericTypographic.Clone();
                StrFormat.Alignment = StringAlignment.Near;
                StrFormat.LineAlignment = StringAlignment.Center;
                StrFormat.FormatFlags |= StringFormatFlags.NoWrap;
            }

            public TextDrawerGdiPlus(Graphics g)
                : base(g)
            {
            }

            public override Size MeasureString(string text, Font font)
            {
                return System.Drawing.Size.Round(this.Graphics.MeasureString(text, font, 0, StrFormat));
            }

            public override void Draw(string text, Font font, Rectangle rowRect, SolidBrush textBrush,
                SolidBrush textBkBrush)
            {
                this.Graphics.FillRectangle(textBkBrush, rowRect);
                this.Graphics.DrawString(text, font, textBrush, rowRect, StrFormat);
            }
        }

        class TextDrawerGdi : TextDrawerBase
        {
            private static readonly TextFormatFlags GdiFlags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping | TextFormatFlags.NoPrefix;
            
            public TextDrawerGdi(Graphics g)
                : base(g)
            {
            }

            public override Size MeasureString(string text, Font font)
            {
                return TextRenderer.MeasureText(this.Graphics, text, font, Size.Empty, GdiFlags);
            }

            public override void Draw(string text, Font font, Rectangle rowRect, SolidBrush textBrush,
                SolidBrush textBkBrush)
            {
                this.Graphics.FillRectangle(textBkBrush, rowRect);
                TextRenderer.DrawText(this.Graphics, text, font, rowRect.Location, textBrush.Color, GdiFlags);
            }
        }

        class TextDrawerTextOut : TextDrawerBase
        {
            [StructLayout(LayoutKind.Sequential)]
            protected struct RECT
            {
                public readonly int l;
                public readonly int t;
                private readonly int r;
                private readonly int b;

                public RECT(System.Drawing.Rectangle r)
                {
                    this.l = r.Left;
                    this.t = r.Top;
                    this.r = r.Right;
                    this.b = r.Bottom;
                }
            }

            [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
            static extern bool TextOut(IntPtr hdc, int nXStart, int nYStart, string lpString, int cbString);
            [DllImport("gdi32.dll")]
            static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
            [DllImport("GDI32.dll")]
            static extern bool DeleteObject(IntPtr objectHandle);
            [DllImport("gdi32.dll")]
            static extern int SetTextColor(IntPtr hdc, int crColor);
            [DllImport("gdi32.dll")]
            static extern bool GetTextExtentPoint(IntPtr hdc, string lpString,
                int cbString, ref Size lpSize);
            [DllImport("user32.dll")]
            static extern int FillRect(IntPtr hDC, [In] ref RECT lprc, IntPtr hbr);
            [DllImport("gdi32.dll")]
            static extern int SetBkMode(IntPtr hdc, int iBkMode);
            [DllImport("gdi32.dll")]
            static extern IntPtr CreateSolidBrush(uint crColor);

            [Flags]
            enum InitState
            {
                None = 0,
                Font = 1,
                Rendered = 2
            }

            private IntPtr hdc;
            private IntPtr oldfont;
            private InitState state;

            private static readonly Dictionary<uint, IntPtr> CachedBrushes = new Dictionary<uint, IntPtr>();
            private static readonly Dictionary<uint, IntPtr> CachedFonts = new Dictionary<uint, IntPtr>();

            protected virtual bool TransparentBackground => true;

            public TextDrawerTextOut(Graphics g)
                : base(g)
            {
                this.hdc = g.GetHdc();
            }

            public override void Dispose()
            {
                if (this.state.HasFlag(InitState.Font))
                    SelectObject(hdc, this.oldfont);

                this.Graphics.ReleaseHdc(this.hdc);

                base.Dispose();
            }

            private static IntPtr MakeBrush(SolidBrush brush)
            {
                var color = (uint)ColorTranslator.ToWin32(brush.Color);
                if (!CachedBrushes.TryGetValue(color, out var handle))
                {
                    handle = CreateSolidBrush(color);
                    CachedBrushes.Add(color, handle);
                }

                return handle;
            }

            private static IntPtr MakeFontCache(Font font)
            {
                int size = (int)font.Size;
                long hi = (long)(((font.Size - size) * 100f)) << 32;
                uint hash = (uint)((long)(font.GetHashCode()) ^ hi);

                if (!CachedFonts.TryGetValue(hash, out var handle))
                {
                    handle = font.ToHfont();
                    CachedFonts.Add(hash, handle);
                }

                return handle;
            }

            internal static void DestroyGdiCache()
            {
                foreach (IntPtr font in CachedFonts.Values)
                    DeleteObject(font);

                foreach (IntPtr brush in CachedBrushes.Values)
                    DeleteObject(brush);
            }

            private void SetupFont(Font font)
            {
                if (!this.state.HasFlag(InitState.Font))
                {
                    this.oldfont = SelectObject(hdc, MakeFontCache(font));
                    this.state |= InitState.Font;
                }
            }

            public override Size MeasureString(string text, Font font)
            {
                SetupFont(font);
                
                var size = new Size();
                if (!GetTextExtentPoint(this.hdc, text, text.Length, ref size))
                    throw new ApplicationException("Failed to compute text dimensions.");

                return size;
            }

            public override void Draw(string text, Font font, Rectangle rowRect, SolidBrush textBrush,
                SolidBrush textBkBrush)
            {
                SetupFont(font);

                if (!this.state.HasFlag(InitState.Rendered))
                {
                    this.state |= InitState.Rendered;
                    if (TransparentBackground)
                        SetBkMode(this.hdc, 1); // TRANSPARENT(1), OPAQUE(2)
                }

                SetTextColor(hdc, ColorTranslator.ToWin32(textBrush.Color));

                RECT rc = new RECT(rowRect);
                Draw(this.hdc, text, font, ref rc, textBkBrush);
            }

            protected virtual void Draw(IntPtr hdc, string text, Font font, ref RECT rc, SolidBrush textBkBrush)
            {
                FillRect(hdc, ref rc, MakeBrush(textBkBrush));
                TextOut(hdc, rc.l, rc.t, text, text.Length);
            }
        }

        class TextDrawerExtTextOut : TextDrawerTextOut
        {

            [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
            static extern bool ExtTextOut(IntPtr hdc, int X, int Y, uint fuOptions,
                [In] ref RECT lprc, [MarshalAs(UnmanagedType.LPWStr)] string lpString, 
                int cbCount, IntPtr lpDx);
            [DllImport("gdi32.dll")]
            static extern int SetBkColor(IntPtr hdc, int crColor);

            public TextDrawerExtTextOut(Graphics g)
                : base(g)
            {
            }
            
            /// <summary>
            /// This has a little performance impact, turn it off because ExtTextOut has that function built in :)
            /// </summary>
            protected override bool TransparentBackground => false;

            protected override void Draw(IntPtr hdc, string text, Font font, ref RECT rc, SolidBrush textBkBrush)
            {
                SetBkColor(hdc, ColorTranslator.ToWin32(textBkBrush.Color));

                // Flags are ETO_OPAQUE(0x2) and ETO_CLIPPED(0x4)
                ExtTextOut(hdc, rc.l, rc.t, 0x2 | 0x4, ref rc, text, text.Length, IntPtr.Zero);
            }
        }

        #endregion Text Drawing

        [DebuggerDisplay("{" + nameof(Text) + "}")]
        class LineInfo
        {
            public int Width = -1;
            public string Text;
        }

        private readonly VScrollBar vScrollBar;
        private readonly HScrollBar hScrollBar;

        private readonly StringFormat strFormat;
        private CircularBuffer<LineInfo> rows;
        private Size textSize = new Size(0, 0);
        private Point originOfText = new Point(0, 0);
        private int lineHeight = -1;
        private int updateCount;
        private int charWidth = -1;
        private int maxRowWidth = -1;
        private int topRow = -1;
        private int bottomRow = -1;
        private int bufferSize = int.MaxValue;
        private int virtualRowCount;
        private bool userScrolling;
        private bool autoScrollAllowed = true;

        private Color evenRowForeColor = Color.Empty;
        private Color evenRowBackColor = Color.Empty;

        private SolidBrush backBrush;
        private SolidBrush foreBrush;
        private SolidBrush evenBackBrush;
        private SolidBrush evenForeBrush;

        private static readonly char[] SplitChars = new[] {'\r', '\n'};

        private static readonly object EvtTopRowChanged = new object();
        private static readonly object EvtBottomRowChanged = new object();
        private static readonly object EvtScrollChanged = new object();

        public InfiniteScrollableControl()
        {
            this.SetStyle(ControlStyles.Opaque, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.DoubleBuffer, true);
            this.SetStyle(ControlStyles.Selectable, true);

            this.strFormat = (StringFormat) StringFormat.GenericTypographic.Clone();
            this.strFormat.Alignment = StringAlignment.Near;
            this.strFormat.LineAlignment = StringAlignment.Center;
            this.strFormat.FormatFlags |= StringFormatFlags.NoWrap;

            this.vScrollBar = new VScrollBar();
            this.vScrollBar.TabIndex = 0;
            this.vScrollBar.Scroll += this.vScrollBar_Scroll;
            this.Controls.Add(this.vScrollBar);

            this.hScrollBar = new HScrollBar();
            this.hScrollBar.TabIndex = 1;
            this.hScrollBar.Scroll += this.hScrollBar_Scroll;
            this.Controls.Add(this.hScrollBar);
        }

        #region Event Handlers
        public event EventHandler TopRowChanged
        {
            add => Events.AddHandler(EvtTopRowChanged, value);
            remove => Events.RemoveHandler(EvtTopRowChanged, value);
        }

        protected virtual void RaiseTopRowChanged(EventArgs e) => ((EventHandler)Events[EvtTopRowChanged])?.Invoke(this, e);

        public event EventHandler BottomRowChanged
        {
            add => Events.AddHandler(EvtBottomRowChanged, value);
            remove => Events.RemoveHandler(EvtBottomRowChanged, value);
        }

        protected virtual void RaiseBottomRowChanged(EventArgs e) => ((EventHandler)Events[EvtBottomRowChanged])?.Invoke(this, e);

        public event EventHandler ScrollChanged
        {
            add => Events.AddHandler(EvtScrollChanged, value);
            remove => Events.RemoveHandler(EvtScrollChanged, value);
        }

        protected virtual void RaiseScrollChanged(EventArgs e) => ((EventHandler)Events[EvtScrollChanged])?.Invoke(this, e);
        #endregion Event Handlers

        [DefaultValue(true)] public bool AutoScrollLastRow { get; set; } = true;
        
        [DefaultValue(nameof(Color.Empty))]
        public Color EvenRowForeColor
        {
            get => this.evenRowForeColor;
            set
            {
                if (this.evenRowForeColor != value)
                {
                    this.evenRowForeColor = value;
                    DestroyBrush(ref this.evenForeBrush);
                    Invalidate();
                }
            }
        }

        [DefaultValue(nameof(Color.Empty))]
        public Color EvenRowBackColor
        {
            get => this.evenRowBackColor;
            set
            {
                if (this.evenRowBackColor != value)
                {
                    this.evenRowBackColor = value;
                    DestroyBrush(ref this.evenBackBrush);
                    Invalidate();
                }
            }
        }

        protected override Padding DefaultPadding => new Padding(10, 10, 8, 8);

        /// <summary>
        /// Gets the top visible row.
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int TopRow
        {
            get => this.topRow;
            set
            {
                if (value < 0)
                    value = 0;

                if (this.topRow != value)
                {
                    var newOrigin = this.originOfText.Y + GetRowRectangle(value).Y;
                    this.originOfText.Y = newOrigin;
                    AdjustScrollBars();
                    Invalidate();
                }
            }
        }

        /// <summary>
        /// Gets the top visible row.
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int BottomRow
        {
            get => this.bottomRow;
            set
            {
                if (value < 0)
                    value = 0;
            
                if (this.bottomRow != value)
                {
                    var newOrigin = (this.originOfText.Y + GetRowRectangle(value).Bottom) - this.ScrollRectangle.Height;
                    this.originOfText.Y = newOrigin;
                    AdjustScrollBars();
                    Invalidate();
                }
            }
        }

        /// <summary>
        /// Gets the row count.
        /// </summary>
        /// <value>
        /// The row count.
        /// </value>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int RowCount
        {
            get
            {
                if (this.rows != null)
                    return this.rows.Count;
                else
                    return 0;
            }
        }

        /// <summary>
        /// Gets the virtual row count, even if the buffer is full, this keeps growing.
        /// </summary>
        /// <value>
        /// The row count.
        /// </value>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int VirtualRowCount
        {
            get
            {
                if (this.rows != null)
                    return this.rows.VirtualCount;
                else
                    return 0;
            }
        }

        // The DisplayRectangle is the interior canvas of the control,
        // so when you have a scrolling control, the DisplayRectangle would be larger than the ClientRectangle,
        // which is only the area of what you see on the screen
        public override Rectangle DisplayRectangle
        {
            get
            {
                var displayRect = new Rectangle(this.Padding.Left, this.Padding.Top,
                    this.textSize.Width + this.Padding.Horizontal, this.textSize.Height + this.Padding.Vertical);
                displayRect.Offset(-this.originOfText.X, -this.originOfText.Y);
                return displayRect;
            }
        }

        /// <summary>
        /// Gets the client area capable of showing scrolling content (excluding visible scrollbars)
        /// </summary>
        /// <value>
        /// The size of the available scroll.
        /// </value>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Rectangle ScrollRectangle
        {
            get
            {
                Rectangle rect = ClientRectangle;
                if (this.vScrollBar.Visible)
                    rect.Width -= this.vScrollBar.Width;
                if (this.hScrollBar.Visible)
                    rect.Height -= this.hScrollBar.Height;
                return rect;
            }
        }

        [DefaultValue(int.MaxValue)]
        public int BufferSize
        {
            get => this.bufferSize;
            set
            {
                value = Math.Max(value, 1);
                this.bufferSize = value;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Not used
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (this.backBrush == null)
                this.backBrush = new SolidBrush(this.BackColor);

            if (this.foreBrush == null)
                this.foreBrush = new SolidBrush(this.ForeColor);

            if (!this.evenRowForeColor.IsEmpty && this.evenForeBrush == null)
                this.evenForeBrush = new SolidBrush(this.evenRowForeColor);

            if (!this.evenRowBackColor.IsEmpty && this.evenBackBrush == null)
                this.evenBackBrush = new SolidBrush(this.evenRowBackColor);
                
            var clientArea = this.ClientRectangle;

            if (!this.textSize.IsEmpty)
            {
                var scrollRect = this.ScrollRectangle;
                var rowsRect = GetRowsRectangle();
                g.FillRectangle(backBrush, rowsRect);

                using (TextDrawerBase drawer = CreateTextDrawer(g))
                {
                    for (int i = this.topRow; i <= this.bottomRow; i++)
                    {
                        Rectangle rowRect = GetRowRectangle(rowsRect, i);

                        var textBrush = this.foreBrush;
                        var textBkBrush = this.backBrush;
                        if ((i % 2) == 0)
                        {
                            if (this.evenForeBrush != null)
                                textBrush = this.evenForeBrush;
                            if (this.evenBackBrush != null)
                                textBkBrush = this.evenBackBrush;
                        }

                        drawer.Draw(this.rows[i].Text, Font, rowRect, textBrush, textBkBrush);
                    }
                }

                // Paint left indent of the content
                var r = new Rectangle(0, 0, rowsRect.Left, clientArea.Height);
                if (r.Width > 0)
                    g.FillRectangle(backBrush, r);

                // Paint top indent of the content
                r = new Rectangle(0, 0, clientArea.Width, rowsRect.Top);
                if (r.Height > 0)
                    g.FillRectangle(backBrush, r);

                // Paint area to right of the content and left of the vertical scroll bar
                r = new Rectangle(rowsRect.Right, scrollRect.Top,
                    scrollRect.Right - rowsRect.Right, scrollRect.Height);
                if (r.Width > 0)
                    g.FillRectangle(backBrush, r);

                // Paint area below the content and above horizontal scroll bar
                r = new Rectangle(scrollRect.Left, rowsRect.Bottom,
                    scrollRect.Width, scrollRect.Bottom - rowsRect.Bottom);
                if (r.Height > 0)
                    g.FillRectangle(backBrush, r);

                if (this.hScrollBar.Visible && this.vScrollBar.Visible)
                {
                    // Paint the small rectangle at the bottom right of the control
                    r = new Rectangle(this.hScrollBar.Right, this.vScrollBar.Bottom,
                        this.ClientRectangle.Width - this.hScrollBar.Right,
                        this.ClientRectangle.Height - this.vScrollBar.Bottom);
                    g.FillRectangle(SystemBrushes.Control, r);
                }
            }
            else
                g.FillRectangle(backBrush, clientArea);

            base.OnPaint(e);
        }

        private TextDrawerBase CreateTextDrawer(Graphics g)
        {
            switch (DrawMode)
            {
                case TextDrawMode.GdiPlus:
                    return new TextDrawerGdiPlus(g);

                case TextDrawMode.Gdi:
                    return new TextDrawerGdi(g);

                case TextDrawMode.TextOut:
                    return new TextDrawerTextOut(g);

                case TextDrawMode.Default:
                case TextDrawMode.ExtTextOut:
                    return new TextDrawerExtTextOut(g);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private Rectangle GetRowRectangle(Rectangle displayRect, int row)
        {
            Rectangle rowRect = displayRect;
            rowRect.Y += row * this.lineHeight;
            rowRect.Height = this.lineHeight;
            return rowRect;
        }

        public Rectangle GetRowsRectangle()
        {
            return new Rectangle(this.Padding.Left - this.originOfText.X,
                this.Padding.Top - this.originOfText.Y, this.textSize.Width, this.textSize.Height);
        }

        public Rectangle GetRowRectangle(int row)
        {
            return GetRowRectangle(GetRowsRectangle(), row);
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            RecalculateTextSize();
        }

        internal void BeginUpdate()
        {
            this.updateCount++;
        }

        internal void EndUpdate()
        {
            if (--this.updateCount == 0)
                RecalculateTextSize();
        }

        /// <summary>Raises the <see cref="E:System.Windows.Forms.Control.FontChanged" /> event.</summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> that contains the event data. </param>
        protected override void OnFontChanged(EventArgs e)
        {
            this.charWidth = -1;
            this.lineHeight = -1;
            RecalculateTextSize();

            base.OnFontChanged(e);
        }

        private void RecalculateTextSize()
        {
            if (this.updateCount != 0 || !this.IsHandleCreated || this.rows == null)
                return;

            bool force = this.lineHeight == -1;
                
            this.maxRowWidth = 0;
            Graphics graphics = null;
            TextDrawerBase textDrawer = null;
            try
            {
                for (int i = this.rows.Count - 1; i >= 0; i--)
                {
                    var line = this.rows[i];
                    if (line.Width == -1 || force)
                    {
                        if (graphics == null)
                        {
                            graphics = this.CreateGraphics();
                            textDrawer = CreateTextDrawer(graphics);
                        }

                        var size = textDrawer.MeasureString(line.Text, Font);
                        if (this.lineHeight == -1)
                            this.lineHeight = size.Height;
                        if (this.charWidth == -1)
                            this.charWidth = size.Width / line.Text.Length;
                        line.Width = size.Width;
                    }
                    if (this.maxRowWidth < line.Width)
                        this.maxRowWidth = line.Width;
                }
            }
            finally
            {
                textDrawer?.Dispose();
                graphics?.Dispose();
            }

            if (this.maxRowWidth == 0)
                this.textSize = Size.Empty;
            else
                this.textSize = new Size(this.maxRowWidth, this.rows.Count * this.lineHeight);

            AdjustScrollBars();
            Invalidate();

            if (this.AutoScrollLastRow && !this.userScrolling && this.autoScrollAllowed)
            {
                this.BottomRow = this.RowCount;
            }
        }

        public void AppendText(string text)
        {
            MakeBuffer();

            int idx = text.IndexOfAny(SplitChars);
            if (idx >= 0)
            {
                foreach (string line in text.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries))
                {
                    this.rows.Add(new LineInfo() {Text = $"{this.virtualRowCount}: {line}"});
                    this.virtualRowCount++;
                }
            }
            else
            {
                this.rows.Add(new LineInfo() {Text = $"{this.virtualRowCount}: {text}"});
                this.virtualRowCount++;
            }

            RecalculateTextSize();
        }

        private void MakeBuffer()
        {
            if (this.rows == null)
                this.rows = new CircularBuffer<LineInfo>(this.bufferSize);
        }

        private void AdjustScrollBars()
        {
            bool scrollChanged = true;
            int loopCount = 0;
            while (scrollChanged)
            {
                bool needHScrollBar;
                bool needVScrollBar;
                var displayRect = this.DisplayRectangle;
                var scrollRect = this.ScrollRectangle;
                if (!this.textSize.IsEmpty)
                {
                    int x = Math.Min(this.originOfText.X, (displayRect.Width - scrollRect.Width));
                    x = Math.Max(x, 0);
                    int y = Math.Min(this.originOfText.Y, (displayRect.Height - scrollRect.Height));
                    y = Math.Max(y, 0);
                    
                    this.originOfText = new Point(x, y);

                    // Update the top/bottoms most rows...
                    SetRowExtremes(GetRowAtLocation(Point.Empty, false), GetRowAtLocation(new Point(this.originOfText.X, scrollRect.Bottom), false));

                    if (scrollRect.Width > this.charWidth)
                    {
                        this.hScrollBar.Minimum = 0;
                        this.hScrollBar.Maximum = displayRect.Width - 1;
                        this.hScrollBar.LargeChange = scrollRect.Width;
                        this.hScrollBar.SmallChange = this.charWidth;
                        this.hScrollBar.Value = this.originOfText.X;
                        needHScrollBar = scrollRect.Width < displayRect.Width;
                    }
                    else
                        needHScrollBar = false;

                    if (scrollRect.Height > this.lineHeight)
                    {
                        this.vScrollBar.Minimum = 0;
                        this.vScrollBar.Maximum = displayRect.Height - 1;
                        this.vScrollBar.LargeChange = scrollRect.Height;
                        this.vScrollBar.SmallChange = 1;
                        this.vScrollBar.Value = this.originOfText.Y;
                        needVScrollBar = scrollRect.Height < displayRect.Height;
                    }
                    else
                        needVScrollBar = false;
                }
                else
                {
                    // No content, no need scroll
                    needHScrollBar = false;
                    needVScrollBar = false;
                }

                this.vScrollBar.Visible = needVScrollBar;
                this.hScrollBar.Visible = needHScrollBar;

                scrollChanged = (needVScrollBar != this.vScrollBar.Visible) ||
                                (needHScrollBar != this.hScrollBar.Visible);
                if (scrollChanged)
                {
                    Invalidate(true);
                    if (++loopCount > 2)
                        break;
                }
            }

            if (this.vScrollBar.Visible)
            {
                Rectangle vScrollBounds = ClientRectangle;
                vScrollBounds.X = vScrollBounds.Right - SystemInformation.VerticalScrollBarWidth;
                vScrollBounds.Width = SystemInformation.VerticalScrollBarWidth;
                if (this.hScrollBar.Visible)
                    vScrollBounds.Height -= SystemInformation.HorizontalScrollBarHeight;
                this.vScrollBar.Bounds = vScrollBounds;
            }

            if (this.hScrollBar.Visible)
            {
                Rectangle hScrollBounds = ClientRectangle;
                hScrollBounds.Y = hScrollBounds.Bottom - SystemInformation.HorizontalScrollBarHeight;
                hScrollBounds.Height = SystemInformation.HorizontalScrollBarHeight;
                if (this.vScrollBar.Visible)
                    hScrollBounds.Width -= SystemInformation.VerticalScrollBarWidth;
                this.hScrollBar.Bounds = hScrollBounds;
            }
        }

        private void SetRowExtremes(int topRow, int bottomRow)
        {
            if (this.topRow != topRow)
            {
                this.topRow = topRow;
                RaiseTopRowChanged(EventArgs.Empty);
            }

            if (this.bottomRow != bottomRow)
            {
                this.bottomRow = bottomRow;
                RaiseBottomRowChanged(EventArgs.Empty);
            }
        }

        private static int AlignValue(int value, int alignment)
        {
            return ((int) value + alignment - 1) / alignment * alignment;
        }

        private void vScrollBar_Scroll(object sender, System.Windows.Forms.ScrollEventArgs e)
        {
            switch (e.Type)
            {
                case ScrollEventType.SmallIncrement:
                    goto case ScrollEventType.LargeDecrement;
                case ScrollEventType.LargeIncrement:
                    goto case ScrollEventType.LargeDecrement;
                case ScrollEventType.SmallDecrement:
                    goto case ScrollEventType.LargeDecrement;
                case ScrollEventType.LargeDecrement:
                    this.originOfText = new Point(originOfText.X, e.NewValue);
                    this.userScrolling = true;
                    AdjustScrollBars();
                    this.userScrolling = false;
                    Invalidate();
                    e.NewValue = this.originOfText.Y;
                    break;
                case ScrollEventType.EndScroll:
                    this.userScrolling = false;
                    goto default;
                case ScrollEventType.ThumbTrack:
                    this.userScrolling = true;
                    goto default;
                default:
                    e.NewValue = AlignValue(e.NewValue, this.vScrollBar.SmallChange);
                    this.originOfText = new Point(this.originOfText.X, e.NewValue);
                    AdjustScrollBars();
                    Invalidate();
                    e.NewValue = this.originOfText.Y;
                    if (e.NewValue + this.ScrollRectangle.Height == this.DisplayRectangle.Height)
                        this.autoScrollAllowed = true;
                    else
                        this.autoScrollAllowed = false;
                    break;
            }

            RaiseScrollChanged(EventArgs.Empty);
        }

        private void hScrollBar_Scroll(object sender, System.Windows.Forms.ScrollEventArgs e)
        {
            switch (e.Type)
            {
                case ScrollEventType.SmallIncrement:
                    goto case ScrollEventType.LargeDecrement;
                case ScrollEventType.LargeIncrement:
                    goto case ScrollEventType.LargeDecrement;
                case ScrollEventType.SmallDecrement:
                    goto case ScrollEventType.LargeDecrement;
                case ScrollEventType.LargeDecrement:
                    this.originOfText = new Point(e.NewValue, this.originOfText.Y);
                    AdjustScrollBars();
                    Invalidate();
                    e.NewValue = this.originOfText.X;
                    break;
                case ScrollEventType.EndScroll:
                    this.userScrolling = false;
                    goto default;
                case ScrollEventType.ThumbTrack:
                    this.userScrolling = true;
                    goto default;
                default:
                    this.originOfText = new Point(e.NewValue, this.originOfText.Y);
                    AdjustScrollBars();
                    Invalidate();
                    e.NewValue = this.originOfText.X;
                    break;
            }

            RaiseScrollChanged(EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            AdjustScrollBars();
            base.OnResize(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            TopRow += e.Delta < 0 ? 1 : -1;
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Right:
                case Keys.Left:
                case Keys.Up:
                case Keys.Down:
                    return true;
                case Keys.Shift | Keys.Right:
                case Keys.Shift | Keys.Left:
                case Keys.Shift | Keys.Up:
                case Keys.Shift | Keys.Down:
                    return true;
            }

            return base.IsInputKey(keyData);
        }

        /// <summary>
        /// Get a line indexes from point.
        /// </summary>
        /// <param name="location">The location point (in client are coordinates).</param>
        /// <param name="strict">if set to <c>true</c> the point should be withing the rows rectangle, otherwise it will fail.</param>
        /// <returns>
        /// Return the row index if any, -1 otherwise.
        /// </returns>
        public int GetRowAtLocation(Point location, bool strict)
        {
            var rowsRect = GetRowsRectangle();
            if (strict && !rowsRect.Contains(location))
                return -1;

            // Get the first row rectangle, than calculate the offset
            var firstRow = GetRowRectangle(rowsRect, 0);
            int offset = location.Y - firstRow.Y;
            return Math.Min(offset / this.lineHeight, this.rows.Count - 1);
        }

        /// <summary>
        /// Ensures that the specified item is visible within the control, scrolling the contents of the control if necessary.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="partialOk">if set to <c>true</c> partial visibility is ok.</param>
        public void EnsureVisible(int row, bool partialOk)
        {
            if (row < this.topRow)
                this.TopRow = row;
            else if (!partialOk && row == this.topRow)
            {
                // Make it fully visible
                var newOrigin = this.originOfText.Y + GetRowRectangle(row).Y;
                if (this.originOfText.Y > newOrigin)
                {
                    this.originOfText.Y = newOrigin;
                    AdjustScrollBars();
                    Invalidate();
                }
            }
            else if (row > this.bottomRow)
                this.BottomRow = row;
            else if (!partialOk && row == this.bottomRow)
            {
                // Make it fully visible
                var newOrigin = (this.originOfText.Y + GetRowRectangle(row).Bottom) - this.ScrollRectangle.Height;
                if (this.originOfText.Y < newOrigin)
                {
                    this.originOfText.Y = newOrigin;
                    AdjustScrollBars();
                    Invalidate();
                }
            }
        }

        /// <summary>Releases the unmanaged resources used by the <see cref="T:System.Windows.Forms.Control" /> and its child controls and optionally releases the managed resources.</summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources. </param>
        protected override void Dispose(bool disposing)
        {
            TextDrawerTextOut.DestroyGdiCache();
            DestroyBrushes();
            base.Dispose(disposing);
        }

        /// <summary>Raises the <see cref="E:System.Windows.Forms.Control.ForeColorChanged" /> event.</summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> that contains the event data. </param>
        protected override void OnForeColorChanged(EventArgs e)
        {
            DestroyBrush(ref this.foreBrush);
            base.OnForeColorChanged(e);
        }

        /// <summary>Raises the <see cref="E:System.Windows.Forms.Control.BackColorChanged" /> event.</summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> that contains the event data. </param>
        protected override void OnBackColorChanged(EventArgs e)
        {
            DestroyBrush(ref this.backBrush);
            base.OnBackColorChanged(e);
        }

        private void DestroyBrushes()
        {
            DestroyBrush(ref this.backBrush);
            DestroyBrush(ref this.foreBrush);
            DestroyBrush(ref this.evenBackBrush);
            DestroyBrush(ref this.evenForeBrush);
        }

        private static void DestroyBrush(ref SolidBrush brush)
        {
            if (brush != null)
            {
                brush.Dispose();
                brush = null;
            }
        }

        public void Clear()
        {
            this.rows?.Clear();
            this.topRow = -1;
            this.bottomRow = -1;
            RecalculateTextSize();
            SetRowExtremes(-1, -1);
        }

        /// <summary>
        /// Gets a value indicating whether the user is scrolling.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is scrolling; otherwise, <c>false</c>.
        /// </value>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsScrolling => this.userScrolling;

        /// <summary>
        /// Gets or sets the draw mode.
        /// </summary>
        /// <value>
        /// The draw mode.
        /// </value>
        [DefaultValue(TextDrawMode.Default)]
        public TextDrawMode DrawMode { get; set; }
    }
}
