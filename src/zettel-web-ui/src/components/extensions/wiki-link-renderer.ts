import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'

/**
 * A ProseMirror plugin that finds [[wiki links]] in text nodes
 * and wraps them with a CSS class for styling.
 *
 * For the full clickable experience, use the WikiLinkView
 * React component which handles click navigation and hover
 * previews on rendered HTML content.
 */

const wikiLinkPluginKey = new PluginKey('wikiLinkRenderer')

const WIKI_LINK_REGEX = /\[\[([^\]]+)\]\]/g

export const WikiLinkRenderer = Extension.create({
  name: 'wikiLinkRenderer',

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key: wikiLinkPluginKey,
        props: {
          decorations(state) {
            const decorations: Decoration[] = []
            const doc = state.doc

            doc.descendants((node, pos) => {
              if (!node.isText || !node.text) return

              const text = node.text

              WIKI_LINK_REGEX.lastIndex = 0
              let match = WIKI_LINK_REGEX.exec(text)
              while (match !== null) {
                const start = pos + match.index
                const end = start + match[0].length
                decorations.push(
                  Decoration.inline(start, end, {
                    class: 'wiki-link',
                    'data-wiki-title': match[1],
                  }),
                )
                match = WIKI_LINK_REGEX.exec(text)
              }
            })

            return DecorationSet.create(doc, decorations)
          },
        },
      }),
    ]
  },
})
