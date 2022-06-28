using System.Collections.Generic;
using System.Threading.Tasks;
using Repository.Infrastructure;
using Repository.Repositories.Currencies;

namespace Service.Services.Currency
{
	public interface ICurrencyService
    {
        Task<IEnumerable<Core.Models.Currency.Currency>> GetAll();
        Task<Core.Models.Currency.Currency> GetById(string id);
        Task<Core.Models.Currency.Currency> Add(Core.Models.Currency.Currency currency);
        Task Update(Core.Models.Currency.Currency currency);
        Task Delete(string id);
    }

    public class CurrencyService : ICurrencyService
    {

        private readonly ICurrencyRepository _currencyRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CurrencyService(ICurrencyRepository currencyRepository, IUnitOfWork unitOfWork)
        {
            _currencyRepository = currencyRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IEnumerable<Core.Models.Currency.Currency>> GetAll()
        {
            await using var transaction = _unitOfWork.BeginTransaction();
            try
            {
                var result = await _currencyRepository.GetAll();
                return result;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<Core.Models.Currency.Currency> GetById(string id)
        {
            await using var transaction = _unitOfWork.BeginTransaction();
            try
            {
                var result = await _currencyRepository.GetById(id);
                return result;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<Core.Models.Currency.Currency> Add(Core.Models.Currency.Currency currency)
        {
            await using var transaction = _unitOfWork.BeginTransaction();
            try
            {
                var result = await _currencyRepository.Add(currency);
                _unitOfWork.SaveChanges();
                return result;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task Update(Core.Models.Currency.Currency currency)
        {
            await using var transaction = _unitOfWork.BeginTransaction();
            try
            {
                await _currencyRepository.Update(currency);
                _unitOfWork.SaveChanges();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task Delete(string id)
        {
            await using var transaction = _unitOfWork.BeginTransaction();
            try
            {
                await _currencyRepository.Delete(id);
                _unitOfWork.SaveChanges();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
