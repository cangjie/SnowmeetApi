const assert = require('node:assert/strict');

// 新建食材档案：先选一级分类，二级分类只列该大类下的启用小类，不能跳过一级直接选二级。
module.exports = async ({ page, go, calls, data }) => {
  const originalCategories = data.categories.slice();
  data.categories.push(
    { id: 101, level: 1, name: '粮油', valid: true, enabled: true },
    { id: 102, level: 2, parent_id: 101, name: '进口面粉', measure_type: 'weight', valid: true, enabled: true },
    { id: 103, level: 2, parent_id: 1, name: '奶粉', measure_type: 'weight', valid: true, enabled: true },
    { id: 105, level: 1, name: '停用大类', valid: true, enabled: false },
    { id: 106, level: 2, parent_id: 105, name: '停用大类下的小类', valid: true, enabled: true },
    { id: 107, level: 2, parent_id: 1, name: '停用小类', valid: true, enabled: false },
    { id: 108, level: 1, name: '没有小类的大类', valid: true, enabled: true }
  );
  const control = name => page.locator('#modal [name=' + name + ']');
  const options = name => control(name).locator('option').evaluateAll(xs => xs.map(x => x.value));
  const created = () => calls.filter(c => c.path.endsWith('/CreateItem'));
  try {
    await go(page, 'routes?item=23');
    await page.getByRole('button', { name: '+ 新建食材档案' }).click();
    assert.deepEqual(await page.locator('#modal select').evaluateAll(xs => xs.slice(0, 2).map(x => x.name)), ['parentCategoryId', 'categoryId']);
    assert.deepEqual(await options('parentCategoryId'), ['', '1', '101']);
    assert.equal(await control('parentCategoryId').inputValue(), '');
    assert.equal(await control('categoryId').isDisabled(), true);
    assert.deepEqual(await options('categoryId'), ['']);
    assert.deepEqual(await options('baseUnitCode'), []);

    await control('parentCategoryId').selectOption('1');
    assert.equal(await control('categoryId').isDisabled(), false);
    assert.deepEqual(await options('categoryId'), ['', '2', '103']);
    assert.equal(await control('categoryId').inputValue(), '');
    await control('categoryId').selectOption('103');
    assert.deepEqual(await options('baseUnitCode'), ['g']);

    await control('parentCategoryId').selectOption('101');
    assert.deepEqual(await options('categoryId'), ['', '102']);
    assert.equal(await control('categoryId').inputValue(), '102');
    assert.deepEqual(await options('baseUnitCode'), ['g']);

    await control('name').fill('全麦粉'); await control('finalFormName').fill('全麦粉');
    await control('parentCategoryId').selectOption('');
    assert.equal(await control('categoryId').isDisabled(), true);
    const before = created().length;
    await page.locator('[data-form=item] [type=submit]').click(); await page.waitForTimeout(150);
    assert.equal(created().length, before, '未选一级分类不能提交');

    await control('parentCategoryId').selectOption('101');
    await page.locator('[data-form=item] [type=submit]').click(); await page.locator('#modal').waitFor({ state: 'hidden' });
    const body = created().at(-1).body;
    assert.equal(body.categoryId, 102); assert.equal(body.baseUnitCode, 'g'); assert.equal('parentCategoryId' in body, false);
  } finally {
    data.categories = originalCategories;
  }
};
