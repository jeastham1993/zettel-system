/**
 * Content transformation utilities: HTML <-> Markdown conversion,
 * plain text extraction, and content truncation.
 */

/** Decode common HTML entities to their text equivalents. */
function decodeEntities(text: string): string {
  return text
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&nbsp;/g, ' ')
}

/**
 * Convert HTML to plain text for previews / snippets.
 * Strips all tags and decodes entities.
 */
export function htmlToPlainText(html: string): string {
  return html
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<\/p>/gi, '\n\n')
    .replace(/<\/h[1-6]>/gi, '\n\n')
    .replace(/<\/li>/gi, '\n')
    .replace(/<li>/gi, '- ')
    .replace(/<[^>]*>/g, '')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&nbsp;/g, ' ')
    .replace(/\n{3,}/g, '\n\n')
    .trim()
}

/**
 * Convert HTML back to Markdown, preserving formatting.
 * Used when loading a note into the editor so users can continue
 * editing with the same markdown they originally wrote.
 */
export function htmlToMarkdown(html: string): string {
  let md = html

  // Code blocks: <pre><code>...</code></pre> → ```...```
  md = md.replace(/<pre><code>([\s\S]*?)<\/code><\/pre>/gi, (_m, code) => {
    return '\n```\n' + decodeEntities(code).trim() + '\n```\n'
  })

  // Inline code: <code>...</code> → `...`
  md = md.replace(/<code>(.*?)<\/code>/gi, (_m, code) => '`' + decodeEntities(code) + '`')

  // Headings: <h1>...</h1> → # ...
  md = md.replace(/<h([1-6])>(.*?)<\/h\1>/gi, (_m, level, text) => {
    return '\n' + '#'.repeat(Number(level)) + ' ' + text.trim() + '\n'
  })

  // Blockquotes: <blockquote>...</blockquote> → > ...
  md = md.replace(/<blockquote>([\s\S]*?)<\/blockquote>/gi, (_m, inner) => {
    const text = inner.replace(/<\/?p>/gi, '').trim()
    return '\n' + text.split('\n').map((line: string) => '> ' + line.trimStart()).join('\n') + '\n'
  })

  // Horizontal rules
  md = md.replace(/<hr\s*\/?>/gi, '\n---\n')

  // Ordered lists
  md = md.replace(/<ol>([\s\S]*?)<\/ol>/gi, (_m, inner) => {
    let idx = 0
    return '\n' + inner.replace(/<li>([\s\S]*?)<\/li>/gi, (_li: string, text: string) => {
      idx++
      return idx + '. ' + text.trim() + '\n'
    }).replace(/<[^>]*>/g, '').trim() + '\n'
  })

  // Unordered lists
  md = md.replace(/<ul>([\s\S]*?)<\/ul>/gi, (_m, inner) => {
    return '\n' + inner.replace(/<li>([\s\S]*?)<\/li>/gi, (_li: string, text: string) => {
      return '- ' + text.trim() + '\n'
    }).replace(/<[^>]*>/g, '').trim() + '\n'
  })

  // Links: <a href="url">text</a> → [text](url)
  md = md.replace(/<a\s+href="([^"]*)"[^>]*>(.*?)<\/a>/gi, (_m, href, text) => `[${text}](${href})`)

  // Bold: <strong> or <b>
  md = md.replace(/<(strong|b)>(.*?)<\/\1>/gi, (_m, _tag, text) => `**${text}**`)

  // Italic: <em> or <i>
  md = md.replace(/<(em|i)>(.*?)<\/\1>/gi, (_m, _tag, text) => `*${text}*`)

  // Paragraphs and line breaks
  md = md.replace(/<br\s*\/?>/gi, '\n')
  md = md.replace(/<\/p>/gi, '\n\n')
  md = md.replace(/<p>/gi, '')

  // Strip any remaining tags
  md = md.replace(/<[^>]*>/g, '')

  // Decode entities
  md = decodeEntities(md)

  // Collapse excessive blank lines
  md = md.replace(/\n{3,}/g, '\n\n')

  return md.trim()
}

/**
 * Convert Markdown to HTML for storage and rendering.
 * Handles headings, bold, italic, links, code blocks, inline code,
 * ordered/unordered lists, blockquotes, and horizontal rules.
 */
export function markdownToHtml(markdown: string): string {
  // First, extract fenced code blocks so their contents are not processed
  const codeBlocks: string[] = []
  let processed = markdown.replace(/```([\s\S]*?)```/g, (_m, code) => {
    codeBlocks.push(code)
    return `\x00CODEBLOCK${codeBlocks.length - 1}\x00`
  })

  // Process inline formatting on a single line
  function inlineFormat(text: string): string {
    // Inline code (process first to protect contents)
    text = text.replace(/`([^`]+)`/g, '<code>$1</code>')
    // Bold
    text = text.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    // Italic
    text = text.replace(/\*(.+?)\*/g, '<em>$1</em>')
    // Links
    text = text.replace(/\[([^\]]+)]\(([^)]+)\)/g, '<a href="$2">$1</a>')
    return text
  }

  const html = processed
    .split(/\n\n+/)
    .map((block) => {
      const trimmed = block.trim()
      if (!trimmed) return ''

      // Restore code blocks
      const codeMatch = trimmed.match(/^\x00CODEBLOCK(\d+)\x00$/)
      if (codeMatch) {
        return `<pre><code>${codeBlocks[Number(codeMatch[1])]}</code></pre>`
      }

      // Horizontal rule
      if (/^(-{3,}|\*{3,}|_{3,})$/.test(trimmed)) {
        return '<hr>'
      }

      // Heading
      const headingMatch = trimmed.match(/^(#{1,6})\s+(.+)$/)
      if (headingMatch) {
        const level = headingMatch[1].length
        return `<h${level}>${inlineFormat(headingMatch[2])}</h${level}>`
      }

      // Blockquote
      if (trimmed.split('\n').every((line) => line.startsWith('> '))) {
        const inner = trimmed
          .split('\n')
          .map((line) => inlineFormat(line.slice(2)))
          .join('<br>')
        return `<blockquote><p>${inner}</p></blockquote>`
      }

      // Unordered list
      if (trimmed.split('\n').every((line) => /^[-*]\s/.test(line))) {
        const items = trimmed
          .split('\n')
          .map((line) => `<li>${inlineFormat(line.replace(/^[-*]\s+/, ''))}</li>`)
          .join('')
        return `<ul>${items}</ul>`
      }

      // Ordered list
      if (trimmed.split('\n').every((line) => /^\d+\.\s/.test(line))) {
        const items = trimmed
          .split('\n')
          .map((line) => `<li>${inlineFormat(line.replace(/^\d+\.\s+/, ''))}</li>`)
          .join('')
        return `<ol>${items}</ol>`
      }

      // Default: paragraph
      return `<p>${inlineFormat(trimmed.replace(/\n/g, '<br>'))}</p>`
    })
    .filter(Boolean)
    .join('')

  return html
}

/**
 * Truncate content for preview cards.
 * Strips HTML tags and clips to a word boundary.
 */
export function truncateContent(content: string, maxLength = 120): string {
  const text = content.replace(/<[^>]*>/g, '')
  if (text.length <= maxLength) return text
  const truncated = text.slice(0, maxLength)
  const lastSpace = truncated.lastIndexOf(' ')
  return (lastSpace > maxLength * 0.6 ? truncated.slice(0, lastSpace) : truncated) + '...'
}
