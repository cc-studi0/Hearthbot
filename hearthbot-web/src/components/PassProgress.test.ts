// @vitest-environment happy-dom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PassProgress from './PassProgress.vue'

describe('PassProgress', () => {
  it('渲染等级与 XP 文本', () => {
    const wrapper = mount(PassProgress, {
      props: { level: 87, xp: 500, xpNeeded: 1000 }
    })
    expect(wrapper.text()).toContain('Lv.87')
    expect(wrapper.text()).toContain('500 / 1000 XP')
  })

  it('xp=500, xpNeeded=1000 → 进度 50%', () => {
    const wrapper = mount(PassProgress, {
      props: { level: 10, xp: 500, xpNeeded: 1000 }
    })
    const fill = wrapper.find('.pass-bar-fill')
    expect(fill.attributes('style')).toContain('width: 50%')
  })

  it('xpNeeded=0 时进度为 0（不 NaN）', () => {
    const wrapper = mount(PassProgress, {
      props: { level: 100, xp: 0, xpNeeded: 0 }
    })
    const fill = wrapper.find('.pass-bar-fill')
    expect(fill.attributes('style')).toContain('width: 0%')
  })

  it('xp 超过 xpNeeded 时钳位到 100', () => {
    const wrapper = mount(PassProgress, {
      props: { level: 5, xp: 5000, xpNeeded: 1000 }
    })
    const fill = wrapper.find('.pass-bar-fill')
    expect(fill.attributes('style')).toContain('width: 100%')
  })

  it('负 xp 钳位到 0', () => {
    const wrapper = mount(PassProgress, {
      props: { level: 1, xp: -10, xpNeeded: 1000 }
    })
    const fill = wrapper.find('.pass-bar-fill')
    expect(fill.attributes('style')).toContain('width: 0%')
  })
})
