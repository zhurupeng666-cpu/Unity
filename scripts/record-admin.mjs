// Real browser recording against a real Go process. API-created fixtures are
// explicitly labelled; this video does not claim to demonstrate a phone or AR.
import { chromium } from '../frontend/node_modules/playwright/index.mjs'
import { spawn } from 'node:child_process'
import { mkdir, copyFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const root = resolve(import.meta.dirname, '..')
const binary = resolve(process.argv[2] || `${root}/work/inspection.exe`)
const base = 'http://127.0.0.1:18082'
const data = resolve(root, `work/admin-demo-${Date.now()}.json`)
let server
const wait = ms => new Promise(r => setTimeout(r, ms))
async function start() {
  server = spawn(binary, ['-addr', '127.0.0.1:18082', '-data', data, '-web', resolve(root, 'frontend/dist')], { windowsHide: true, stdio: ['ignore', 'ignore', 'inherit'] })
  for (let i = 0; i < 100; i++) {
    if (server.exitCode !== null) throw new Error(`Server exited with ${server.exitCode}`)
    try { if ((await fetch(`${base}/health`)).ok) return } catch {}
    await wait(100)
  }
  throw new Error('Server did not start')
}
async function stop() {
  if (!server || server.exitCode !== null) return
  const exit = new Promise(r => server.once('exit', r))
  server.kill('SIGTERM'); await exit
}
await mkdir(resolve(root, 'work/recordings'), { recursive: true })
await mkdir(resolve(root, 'demo'), { recursive: true })
const browser = await chromium.launch()
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, recordVideo: { dir: resolve(root, 'work/recordings'), size: { width: 1440, height: 1000 } } })
const page = await context.newPage()
const began = Date.now()
async function chapter(text, seconds = 12) {
  await page.evaluate(text => {
    document.getElementById('demo-caption')?.remove()
    const box = document.createElement('div'); box.id = 'demo-caption'; box.textContent = text
    Object.assign(box.style, { position: 'fixed', bottom: '18px', left: '50%', transform: 'translateX(-50%)', zIndex: '99999', maxWidth: '90vw', padding: '15px 26px', background: '#132d2bf0', color: '#fff', border: '1px solid #8fae82', borderRadius: '10px', font: '16px Microsoft YaHei, sans-serif', textAlign: 'center', pointerEvents: 'none' })
    document.body.append(box)
  }, text)
  await wait(seconds * 1000)
}
try {
  await start()
  await page.goto(base)
  await chapter('电脑端演示｜真实 Go 服务 + React 管理台。此视频不包含手机真机 AR。', 14)
  await chapter('① 初始为空列表。稍后通过 API 创建演示记录，明确区别于手机上报。', 12)
  for (const [title, priority, description] of [
    ['入口墙面破损', 'high', '左侧墙体存在裂缝，需要安排现场复查。'],
    ['配电箱指示灯异常', 'medium', '运行指示灯间歇闪烁。'],
    ['通道标识褪色', 'low', '建议在下次维护时更换。'],
  ]) {
    const response = await fetch(`${base}/api/issues`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ title, priority, description, position: { x: .42, y: .03, z: 1.26 } }) })
    if (response.status !== 201) throw new Error(await response.text())
  }
  await chapter('② 三条测试记录已通过真实 POST /api/issues 保存，点击刷新查看。', 8)
  await page.getByRole('button', { name: '刷新列表' }).click()
  await page.getByRole('button', { name: '入口墙面破损', exact: true }).waitFor()
  await page.screenshot({ path: resolve(root, 'demo/admin.png'), fullPage: true })
  await chapter('列表显示标题、优先级、处理状态与上报时间；数据来自同一个 Go 后端。', 12)
  await page.getByRole('button', { name: '入口墙面破损', exact: true }).click()
  await chapter('③ 展开详情：可查看描述、AR 会话坐标、完整 ID 和更新时间。', 12)
  const status = page.getByRole('combobox', { name: '入口墙面破损的状态' })
  await status.selectOption('in_progress')
  await page.getByRole('status').waitFor()
  await chapter('④ 状态从 open 改为 in_progress，后端成功保存后页面显示确认。', 14)
  await status.selectOption('resolved')
  await chapter('状态改为 resolved；统计卡片同步变化。', 10)
  await page.getByRole('combobox', { name: '筛选状态' }).selectOption('resolved')
  await chapter('⑤ 按状态筛选，只显示已经解决的问题。', 10)
  await page.getByRole('combobox', { name: '筛选状态' }).selectOption('all')
  await page.getByRole('textbox', { name: '搜索问题' }).fill('配电箱')
  await chapter('搜索标题或描述，快速定位现场问题。', 10)
  await page.getByRole('textbox', { name: '搜索问题' }).fill('')
  await stop()
  await page.getByRole('button', { name: '刷新列表' }).click()
  await page.getByRole('alert').waitFor()
  await chapter('⑥ 现在真实停止 Go 进程。刷新出现错误提示，已有页面和数据仍可见。', 15)
  await start()
  await page.getByRole('button', { name: '重新加载' }).click()
  await page.getByRole('alert').waitFor({ state: 'detached' })
  if (await status.inputValue() !== 'resolved') throw new Error('status not persisted across restart')
  await chapter('⑦ 使用相同 JSON 文件重启 Go。重新加载后问题仍存在，状态保持 resolved。', 15)
  await page.reload()
  await status.waitFor()
  await chapter('浏览器整页刷新后，仍读取到已保存的记录与处理状态。', 12)
  await status.selectOption('open')
  await chapter('三种状态均可设置：open / in_progress / resolved。状态更新通过 PATCH 接口保存。', 12)
  await chapter('电脑端验证完成。手机识别平面、触摸放置和手机提交，仍需真实 ARCore 设备另行验收。', 14)
  const remaining = 190000 - (Date.now() - began)
  if (remaining > 0) await wait(remaining)
} finally {
  const video = page.video()
  await context.close(); await browser.close(); await stop()
  if (video) await copyFile(await video.path(), resolve(root, 'demo/admin-demo.webm'))
}
console.log('Saved demo/admin-demo.webm (real browser session, API fixtures explicitly labelled)')
