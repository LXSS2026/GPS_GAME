/* 全ページ共通の動き */
(function () {
  var here = location.pathname.split('/').pop();
  document.querySelectorAll('body > nav a').forEach(function (a) { if (a.getAttribute('href').split('#')[0] === here) a.classList.add('sel'); });
})();
