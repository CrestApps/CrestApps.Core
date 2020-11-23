using CrestApps.Core.Models;
using CrestApps.Data.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CrestApps.Core.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public readonly IUnitOfWork _unitOfWork;

        public HomeController(ILogger<HomeController> logger, IUnitOfWork unitOfWork)
        {
            _logger = logger;
            _unitOfWork = unitOfWork;
        }


        public async Task<IActionResult> Test()
        {
            /*
            var role = new Data.Models.Role()
            {
                Name = "Some name",
                NormalizedName = "Some Name",
            };

            _unitOfWork.Roles.Add(role);
            await _unitOfWork.SaveAsync();
            */

            var roles = await _unitOfWork.Roles.GetAllAsync();

            return Content("worked!");
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
