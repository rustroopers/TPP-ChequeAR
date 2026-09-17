namespace ChequeAR.Application.Security
{
    public interface ISpecialtyGuard
    {
        SpecialtyScope Scope { get; }

        /// <summary>Lanza SpecialtyForbiddenException si el recurso no cae en el scope.</summary>
        void EnsureAllows(string specialtyCode);
    }
}
