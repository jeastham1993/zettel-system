import { Node } from '@tiptap/core'
import Suggestion from '@tiptap/suggestion'
import type { SuggestionOptions } from '@tiptap/suggestion'
import { searchTitles } from '@/api/notes'
import type { TitleSearchResult } from '@/api/types'

export type WikiLinkSuggestionOptions = {
  suggestion: Omit<SuggestionOptions<TitleSearchResult>, 'editor'>
}

export const WikiLinkSuggestion = Node.create<WikiLinkSuggestionOptions>({
  name: 'wikiLinkSuggestion',

  group: 'inline',
  inline: true,
  selectable: false,
  atom: true,

  addOptions() {
    return {
      suggestion: {
        char: '[[',
        items: async ({ query }: { query: string }) => {
          if (query.length === 0) return []
          try {
            return await searchTitles(query)
          } catch {
            return []
          }
        },
        command: ({ editor, range, props }) => {
          const item = props as unknown as TitleSearchResult
          editor
            .chain()
            .focus()
            .deleteRange(range)
            .insertContent(`[[${item.title}]]`)
            .run()
        },
      },
    }
  },

  addProseMirrorPlugins() {
    return [
      Suggestion({
        editor: this.editor,
        ...this.options.suggestion,
      }),
    ]
  },
})
