import { ReactRenderer } from '@tiptap/react'
import tippy from 'tippy.js'
import type { Instance as TippyInstance } from 'tippy.js'
import { WikiLinkPopup } from '@/components/wiki-link-popup'
import type { WikiLinkPopupRef } from '@/components/wiki-link-popup'
import type { SuggestionOptions, SuggestionKeyDownProps } from '@tiptap/suggestion'
import type { TitleSearchResult } from '@/api/types'

export const suggestionRenderer: SuggestionOptions<TitleSearchResult>['render'] =
  () => {
    let component: ReactRenderer<WikiLinkPopupRef> | null = null
    let popup: TippyInstance | null = null

    return {
      onStart: (props) => {
        component = new ReactRenderer(WikiLinkPopup, {
          props,
          editor: props.editor,
        })

        if (!props.clientRect) return

        const getReferenceClientRect = props.clientRect as () => DOMRect

        const [instance] = tippy('body', {
          getReferenceClientRect,
          appendTo: () => document.body,
          content: component.element,
          showOnCreate: true,
          interactive: true,
          trigger: 'manual',
          placement: 'bottom-start',
        })

        popup = instance ?? null
      },

      onUpdate: (props) => {
        component?.updateProps(props)

        if (props.clientRect && popup) {
          popup.setProps({
            getReferenceClientRect: props.clientRect as () => DOMRect,
          })
        }
      },

      onKeyDown: (props: SuggestionKeyDownProps) => {
        if (props.event.key === 'Escape') {
          popup?.hide()
          return true
        }

        return component?.ref?.onKeyDown(props) ?? false
      },

      onExit: () => {
        popup?.destroy()
        component?.destroy()
      },
    }
  }
