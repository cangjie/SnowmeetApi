const assert = require('node:assert/strict');

module.exports = async ({ page, go, data }) => {
  const originalCategories = data.categories.slice(), originalItems = data.moreItems;
  data.categories.push(
    { id: 101, level: 1, name: '粮油', valid: true },
    { id: 102, level: 2, parent_id: 101, name: '进口面粉', valid: true },
    { id: 103, level: 2, parent_id: 1, name: '奶粉', valid: true },
    { id: 104, level: 2, parent_id: 1, name: '奶制半成品', valid: true, is_prepared: true },
    { id: 105, level: 1, name: '停用大类', valid: false },
    { id: 106, level: 2, parent_id: 105, name: '停用大类下的小类', valid: true },
    { id: 107, level: 2, parent_id: 1, name: '停用小类', valid: false }
  );
  data.moreItems = [
    { ...data.item, id: 111, category_id: 102, name: '面粉' },
    { ...data.item, id: 112, category_id: 103, name: '全脂奶粉' },
    { ...data.item, id: 113, category_id: 104, name: '奶冻', item_type: 'prepared' },
    { ...data.item, id: 114, category_id: 106, name: '停用大类的食材' },
    { ...data.item, id: 115, category_id: 107, name: '停用小类的食材' }
  ];
  const options = name => page.locator('[name=' + name + '] option').evaluateAll(xs => xs.map(x => x.value));
  try {
    await go(page, 'inbound');
    assert.deepEqual(await options('parentCategoryId'), ['', '1', '101']);
    assert.deepEqual(await options('itemId'), ['', '23', '111', '112']);
    await page.locator('[name=parentCategoryId]').selectOption('1');
    assert.deepEqual(await options('categoryId'), ['', '2', '103']);
    assert.deepEqual(await options('itemId'), ['', '23', '112']);
    await page.locator('[name=categoryId]').selectOption('103');
    assert.deepEqual(await options('itemId'), ['', '112']);
    await page.locator('[name=categoryId]').selectOption('2');
    await page.locator('[name=itemId]').selectOption('23');
    await page.locator('[name=specId]').waitFor();
    await page.waitForFunction(() => document.querySelector('[name=batchNo]')?.value);
    await page.locator('[name=parentCategoryId]').selectOption('101');
    assert.deepEqual(await options('categoryId'), ['', '102']);
    assert.deepEqual(await options('itemId'), ['', '111']);
    assert.equal(await page.locator('[name=categoryId]').inputValue(), '');
    assert.equal(await page.locator('[name=itemId]').inputValue(), '');
    assert.equal(await page.locator('[name=batchNo]').inputValue(), '');
    assert.equal(await page.locator('[name=specId]').count(), 0);
    assert.equal(await page.locator('#inbound-unit').textContent(), '');
    assert.equal(await page.locator('#inbound-expiry').textContent(), '');
    await page.locator('[name=categoryId]').selectOption('102');
    await page.getByRole('button', { name: '扫一扫', exact: true }).click();
    await page.waitForFunction(() => document.querySelector('[name=specId]')?.value === '31');
    assert.equal(await page.locator('[name=parentCategoryId]').inputValue(), '1');
    assert.equal(await page.locator('[name=categoryId]').inputValue(), '2');
    assert.equal(await page.locator('[name=itemId]').inputValue(), '23');
    assert.deepEqual(await options('categoryId'), ['', '2', '103']);
    assert.deepEqual(await options('itemId'), ['', '23']);
    await page.locator('[name=parentCategoryId]').selectOption('');
    assert.deepEqual(await options('categoryId'), ['', '2', '102', '103']);
    assert.deepEqual(await options('itemId'), ['', '23', '111', '112']);
  } finally {
    data.categories = originalCategories; data.moreItems = originalItems;
  }
};
