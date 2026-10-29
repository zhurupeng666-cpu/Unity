import { test, expect } from "@playwright/test";

test("API create → browser view/filter → status update → reload", async ({
  page,
  request,
}) => {
  const title = `入口墙面破损 ${Date.now()}`;
  const response = await request.post("http://127.0.0.1:8080/api/issues", {
    data: {
      title,
      description: "左侧墙体存在裂缝，请安排维修。",
      priority: "high",
      position: { x: 0.42, y: 0.03, z: 1.26 },
    },
  });
  expect(response.status()).toBe(201);
  const created = await response.json();
  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "现场问题", exact: true }),
  ).toBeVisible();
  await page.getByRole("textbox", { name: "搜索问题" }).fill(title);
  await expect(
    page.getByRole("button", { name: title, exact: true }),
  ).toBeVisible();
  const row = page
    .getByRole("row")
    .filter({ has: page.getByRole("button", { name: title, exact: true }) });
  await expect(row.getByText("高优先级")).toBeVisible();
  await row.getByRole("button", { name: "详情" }).click();
  await expect(page.getByText("左侧墙体存在裂缝，请安排维修。")).toBeVisible();
  for (const status of ["in_progress", "resolved", "open"]) {
    await row.getByRole("combobox").selectOption(status);
    await expect(row.getByRole("combobox")).toHaveValue(status);
    await expect(page.getByRole("status")).toContainText("已更新");
    const all = await (
      await request.get("http://127.0.0.1:8080/api/issues")
    ).json();
    expect(all.find((v: { id: string }) => v.id === created.id).status).toBe(
      status,
    );
  }
  await page.reload();
  await expect(
    page.getByRole("combobox", { name: `${title}的状态` }),
  ).toHaveValue("open");
  await page.getByRole("combobox", { name: "筛选优先级" }).selectOption("low");
  await expect(
    page.getByRole("button", { name: title, exact: true }),
  ).toHaveCount(0);
  await page.getByRole("combobox", { name: "筛选优先级" }).selectOption("high");
  await expect(
    page.getByRole("button", { name: title, exact: true }),
  ).toBeVisible();
});

test("unavailable backend shows recoverable error and keeps page visible", async ({
  page,
  request,
}) => {
  const title = `恢复连接检查 ${Date.now()}`;
  await request.post("http://127.0.0.1:8080/api/issues", {
    data: { title, priority: "low", position: { x: 0, y: 0, z: 0 } },
  });
  await page.route("**/api/issues", (route) =>
    route.abort("connectionrefused"),
  );
  await page.goto("/");
  await expect(page.getByRole("alert")).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "现场问题", exact: true }),
  ).toBeVisible();
  await page.unroute("**/api/issues");
  await page.getByRole("button", { name: "重新加载" }).click();
  await expect(page.getByRole("alert")).toHaveCount(0);
  await expect(
    page.getByRole("columnheader", { name: "优先级", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: title, exact: true }),
  ).toBeVisible();
});

test("failed update preserves saved status", async ({ page, request }) => {
  const title = `设备异常 ${Date.now()}`;
  await request.post("http://127.0.0.1:8080/api/issues", {
    data: { title, priority: "medium", position: { x: 0, y: 0, z: 1 } },
  });
  await page.goto("/");
  const status = page.getByRole("combobox", { name: `${title}的状态` });
  await expect(status).toHaveValue("open");
  await page.route("**/status", (route) =>
    route.fulfill({
      status: 500,
      contentType: "application/json",
      body: JSON.stringify({
        error: { code: "STORAGE_ERROR", message: "could not save status" },
      }),
    }),
  );
  await status.selectOption("resolved");
  await expect(page.getByRole("alert")).toContainText("could not save status");
  await expect(status).toHaveValue("open");
});

test("empty state and mobile layout", async ({ page }) => {
  await page.route("**/api/issues", (route) =>
    route.fulfill({ contentType: "application/json", body: "[]" }),
  );
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "还没有现场问题" }),
  ).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});
