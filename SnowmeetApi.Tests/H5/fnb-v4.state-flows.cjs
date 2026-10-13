const assert = require('node:assert/strict');

module.exports = async ({page, go, calls, data}) => {
  // The operations flow already accepts confirmation dialogs.
  data.categories.push({id:900,level:1,parent_id:null,name:'状态验证分类',valid:true,enabled:true,sort:0});
  await go(page,'cats');
  const category = () => page.locator('details').filter({has:page.locator('summary').filter({hasText:'状态验证分类'})});
  await category().getByRole('button',{name:'停用分类',exact:true}).click();
  await category().getByRole('button',{name:'启用分类',exact:true}).waitFor();
  assert.equal(calls.findLast(c=>c.path.endsWith('/SaveCategory')).body.enabled,false);
  assert.equal(data.categories.find(c=>c.id===900).valid,true);
  await category().getByRole('button',{name:'启用分类',exact:true}).click();
  await category().getByRole('button',{name:'停用分类',exact:true}).waitFor();
  await category().getByRole('button',{name:'删除分类',exact:true}).click();
  await category().waitFor({state:'detached'});
  assert.equal(data.categories.find(c=>c.id===900).valid,false);
  await go(page,'cats');
  assert.equal(await category().count(),0);

  await go(page,'areas');
  await page.getByRole('button',{name:'+ 新增一级区域',exact:true}).click();
  await page.locator('#modal [name=name]').fill('状态验证区域');
  const save = async () => {
    await page.locator('[data-form=op-area] [type=submit]').click();
    await page.locator('#modal').waitFor({state:'hidden'});
    await page.locator('#page').getByText('正在加载…',{exact:true}).waitFor({state:'hidden'});
  };
  await save();
  const card = () => page.locator('section.card').filter({has:page.getByRole('heading',{name:'状态验证区域',exact:true})});
  await card().getByRole('button',{name:'编辑区域',exact:true}).click();
  await page.locator('#modal [name=enabled]').uncheck(); await save();
  assert.equal(calls.findLast(c=>c.path.endsWith('/SaveArea')).body.enabled,false);
  await card().getByText('停用',{exact:true}).waitFor();
  await card().getByRole('button',{name:'编辑区域',exact:true}).click();
  await page.locator('#modal [name=enabled]').check(); await save();
  await card().getByText('启用',{exact:true}).waitFor();
  await card().getByRole('button',{name:'删除空区域',exact:true}).click();
  await card().waitFor({state:'detached'});
  await go(page,'areas'); assert.equal(await card().count(),0);
};
