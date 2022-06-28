using System.Collections.Generic;
using System.Threading.Tasks;
using Repository.Infrastructure;
using Repository.Repositories.Currencies;

namespace Service.Services.Currency
{
	public interface ICurrencyService
    {
        Task<IEnumerable<Currency>> GetAll();
        Task<Currency> GetById(string id);
        Task<Currency> Add(Currency currency);
        Task Update(Currency currency);
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

        public async Task<IEnumerable<Currency>> GetAll()
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

        public async Task<Currency> GetById(string id)
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

        public async Task<Currency> Add(Currency currency)
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

        public async Task Update(Currency currency)
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
