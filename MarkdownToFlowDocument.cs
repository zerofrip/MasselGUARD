using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;

namespace MasselGUARD
{
    /// <summary>
    /// Converts a Markdown string to a WPF FlowDocument.
    /// Handles: H1–H3, paragraphs, unordered lists (nested), thematic breaks (---),
    /// fenced/indented code blocks, and inline bold / italic / inline-code.
    /// </summary>
    internal static class MarkdownToFlowDocument
    {
        private static readonly MarkdownPipeline _pipeline =
            new MarkdownPipelineBuilder().Build();

        // ── Public API ────────────────────────────────────────────────────────

        public static FlowDocument Render(
            string     markdown,
            FontFamily fontFamily,
            double     baseFontSize,
            Brush      textBrush,
            Brush      mutedBrush,
            Brush      accentBrush,
            Brush      codeBgBrush)
        {
            var ctx   = new Ctx(fontFamily, baseFontSize, textBrush, mutedBrush, accentBrush, codeBgBrush);
            var mdDoc = Markdown.Parse(markdown, _pipeline);

            var flow = new FlowDocument
            {
                FontFamily  = fontFamily,
                FontSize    = baseFontSize,
                Foreground  = textBrush,
                PagePadding = new Thickness(4, 2, 4, 2),
                LineHeight  = double.NaN,
            };

            foreach (var block in mdDoc)
                AddBlock(flow.Blocks, block, ctx, 0);

            return flow;
        }

        public static FlowDocument MakeSimple(string text, FontFamily fontFamily, double baseFontSize, Brush brush)
        {
            var flow = new FlowDocument
            {
                FontFamily  = fontFamily,
                FontSize    = baseFontSize,
                PagePadding = new Thickness(4, 2, 4, 2),
            };
            flow.Blocks.Add(new Paragraph(new Run(text)) { Foreground = brush });
            return flow;
        }

        // ── Block rendering ───────────────────────────────────────────────────

        private static void AddBlock(BlockCollection blocks, Markdig.Syntax.Block block, Ctx ctx, int depth)
        {
            switch (block)
            {
                case Markdig.Syntax.HeadingBlock h:
                    blocks.Add(RenderHeading(h, ctx));
                    break;

                case Markdig.Syntax.ParagraphBlock pb:
                    blocks.Add(RenderParagraph(pb, ctx, depth));
                    break;

                case Markdig.Syntax.ListBlock lb:
                    blocks.Add(RenderList(lb, ctx, depth));
                    break;

                case Markdig.Syntax.ThematicBreakBlock:
                    blocks.Add(RenderHR(ctx));
                    break;

                case Markdig.Syntax.FencedCodeBlock fcb:
                    blocks.Add(RenderCodeBlock(fcb.Lines.ToString(), ctx));
                    break;

                case Markdig.Syntax.CodeBlock cb:
                    blocks.Add(RenderCodeBlock(cb.Lines.ToString(), ctx));
                    break;
            }
        }

        private static Paragraph RenderHeading(Markdig.Syntax.HeadingBlock h, Ctx ctx)
        {
            double size = h.Level switch
            {
                1 => ctx.Base + 5,
                2 => ctx.Base + 3,
                _ => ctx.Base + 1,
            };

            var p = new Paragraph
            {
                FontSize   = size,
                FontWeight = FontWeights.SemiBold,
                Foreground = h.Level <= 2 ? ctx.Accent : ctx.Text,
                Margin     = h.Level <= 2
                    ? new Thickness(0, 14, 0, 4)
                    : new Thickness(0, 8, 0, 2),
            };

            if (h.Inline != null)
                AddInlines(p.Inlines, h.Inline, ctx);

            return p;
        }

        private static Paragraph RenderParagraph(Markdig.Syntax.ParagraphBlock pb, Ctx ctx, int depth)
        {
            var p = new Paragraph
            {
                Margin     = depth > 0 ? new Thickness(0) : new Thickness(0, 2, 0, 2),
                Foreground = ctx.Text,
            };

            if (pb.Inline != null)
                AddInlines(p.Inlines, pb.Inline, ctx);

            return p;
        }

        private static List RenderList(Markdig.Syntax.ListBlock lb, Ctx ctx, int depth)
        {
            var list = new List
            {
                MarkerStyle  = TextMarkerStyle.Disc,
                MarkerOffset = 6,
                Padding      = new Thickness(depth == 0 ? 16 : 8, 0, 0, 0),
                Margin       = new Thickness(0, depth == 0 ? 2 : 0, 0, depth == 0 ? 2 : 0),
            };

            foreach (var item in lb)
            {
                if (item is not Markdig.Syntax.ListItemBlock li) continue;

                var listItem = new ListItem { Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(0) };
                foreach (var child in li)
                    AddBlock(listItem.Blocks, child, ctx, depth + 1);
                list.ListItems.Add(listItem);
            }

            return list;
        }

        private static Paragraph RenderHR(Ctx ctx) => new Paragraph
        {
            BorderBrush     = ctx.Muted,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin          = new Thickness(0, 10, 0, 10),
            Padding         = new Thickness(0),
        };

        private static Paragraph RenderCodeBlock(string code, Ctx ctx) =>
            new Paragraph(new Run(code.TrimEnd()))
            {
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize   = ctx.Base - 1,
                Background = ctx.CodeBg,
                Foreground = ctx.Text,
                Padding    = new Thickness(8, 4, 8, 4),
                Margin     = new Thickness(0, 4, 0, 4),
            };

        // ── Inline rendering ──────────────────────────────────────────────────

        private static void AddInlines(InlineCollection inlines,
            Markdig.Syntax.Inlines.ContainerInline container, Ctx ctx)
        {
            foreach (var inline in container)
            {
                switch (inline)
                {
                    case Markdig.Syntax.Inlines.LiteralInline lit:
                        inlines.Add(new Run(lit.Content.ToString()));
                        break;

                    case Markdig.Syntax.Inlines.LineBreakInline lbi:
                        inlines.Add(lbi.IsHard ? (Inline)new LineBreak() : new Run(" "));
                        break;

                    case Markdig.Syntax.Inlines.CodeInline ci:
                    {
                        var span = new Span { Background = ctx.CodeBg };
                        span.Inlines.Add(new Run(ci.Content)
                        {
                            FontFamily = new FontFamily("Consolas, Courier New"),
                            FontSize   = ctx.Base - 1,
                        });
                        inlines.Add(span);
                        break;
                    }

                    case Markdig.Syntax.Inlines.EmphasisInline em:
                    {
                        Span span = em.DelimiterCount >= 2 ? new Bold() : (Span)new Italic();
                        AddInlines(span.Inlines, em, ctx);
                        inlines.Add(span);
                        break;
                    }

                    case Markdig.Syntax.Inlines.ContainerInline ci2:
                        AddInlines(inlines, ci2, ctx);
                        break;
                }
            }
        }

        // ── Render context (short-named for brevity) ──────────────────────────

        private sealed class Ctx
        {
            public FontFamily FontFamily { get; }
            public double     Base       { get; }
            public Brush      Text       { get; }
            public Brush      Muted      { get; }
            public Brush      Accent     { get; }
            public Brush      CodeBg     { get; }

            public Ctx(FontFamily fontFamily, double baseFontSize,
                Brush text, Brush muted, Brush accent, Brush codeBg)
            {
                FontFamily = fontFamily;
                Base       = baseFontSize;
                Text       = text;
                Muted      = muted;
                Accent     = accent;
                CodeBg     = codeBg;
            }
        }
    }
}
