using Ardalis.Specification;

namespace Kafdoc.Domain;

public interface IRepository<T> : IRepositoryBase<T> where T : class;