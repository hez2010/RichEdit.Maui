exports.postTransform = function (model) {
  // Link the complete namespace; its empty parents have no generated pages.
  if (model.namespace && model.namespace.specName) {
    model.namespace.specName.forEach(function (name) {
      name.value = '<xref uid="' + model.namespace.uid + '" />';
    });
  }
  return model;
};
